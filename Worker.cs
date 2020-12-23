#region Using
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
#endregion

namespace cloudflare
{
    public class Worker : BackgroundService
    {        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                string config = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "config.dat"));
                dynamic data = JObject.Parse(config);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await SyncAllAsync();
                    await Task.Delay((int)data.SyncIntervalInMilliseconds, stoppingToken); // Time to wait between syncs. All domains are synced at once when this time expires.
                }
            }
            catch (Exception ex)
            {
                await LogWriteAsync(ex.Message);
            }
        }

        #region Methods      
        protected async Task SyncAllAsync()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "config.dat");

            if (File.Exists(path))
            {
                try
                {
                    string wanip = await InfoWANIPAsync();                    

                    if(wanip != "N/A")
                    { 
                        string configuration = File.ReadAllText(path);

                        dynamic data = JObject.Parse(configuration);
                        dynamic domains = data.Domains;

                        foreach (dynamic domain in domains)
                        {
                            string apikey = Convert.ToString(domain.APIKey);
                            string email = Convert.ToString(domain.Email);

                            string domainName = Convert.ToString(domain.Domain_Name);
                            string dnsRecord = Convert.ToString(domain.DNS_Record);
                            string type = Convert.ToString(domain.Type);
                            string ttl = Convert.ToString(domain.TTL);
                            string proxied = Convert.ToString(domain.Proxied);

                            ServicePointManager.DefaultConnectionLimit = 10;
                            ServicePointManager.Expect100Continue = false;

                            // Get zone-id for the domain name, which is used in the update statement below.
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.cloudflare.com/client/v4/zones?&name=" + domainName);                                
                            req.Proxy = null;
                            req.Accept = "*/*";
                            req.Method = "GET";
                            req.Headers.Add("X-Auth-Email:" + email);
                            req.Headers.Add("X-Auth-Key:" + apikey);
                            req.ContentType = "application/json";

                            HttpWebResponse response = (HttpWebResponse)req.GetResponse();
                            Stream dataStream = response.GetResponseStream();
                            StreamReader reader = new StreamReader(dataStream);

                            string zones = String.Empty;
                            dynamic zones_data = JObject.Parse(await reader.ReadToEndAsync());
                            dynamic results = zones_data.result;

                            reader.Close();
                            response.Close();
                                
                            foreach (dynamic result in results)
                            {
                                zones = Convert.ToString(result.id);
                            }

                            // Get dns_record id for the DNS record, which is used in the update statement below.
                            req = (HttpWebRequest)WebRequest.Create("https://api.cloudflare.com/client/v4/zones/" + zones + "/dns_records?type=" + type + "&name=" + dnsRecord);
                            req.Accept = "*/*";
                            req.Method = "GET";
                            req.Headers.Add("X-Auth-Email:" + email);
                            req.Headers.Add("X-Auth-Key:" + apikey);
                            req.ContentType = "application/json";

                            response = (HttpWebResponse)req.GetResponse();
                            dataStream = response.GetResponseStream();
                            reader = new StreamReader(dataStream);

                            string dns_records = String.Empty;
                            dynamic zone_data = JObject.Parse(await reader.ReadToEndAsync());
                            dynamic zone_results = zone_data.result;

                            reader.Close();
                            response.Close();

                            foreach (dynamic result in zone_results)
                            {
                                dns_records = Convert.ToString(result.id);
                            }

                            // Send zone update.
                            req = (HttpWebRequest)WebRequest.Create("https://api.cloudflare.com/client/v4/zones/" + zones + "/dns_records/" + dns_records);                                
                            req.Accept = "*/*";
                            req.Method = "PUT";
                            req.Headers.Add("X-Auth-Email:" + email);
                            req.Headers.Add("X-Auth-Key:" + apikey);
                            req.ContentType = "application/json";

                            string jsonData = "{\"type\":\"" + type + "\",\"name\":\"" + dnsRecord + "\",\"content\":\"" + wanip + "\",\"ttl\":" + ttl + ",\"proxied\":" + proxied + "}";

                            using (var sw = new StreamWriter(await req.GetRequestStreamAsync()))
                            {
                                sw.Write(jsonData);
                                sw.Flush();
                                sw.Close();
                            }

                            response = (HttpWebResponse)req.GetResponse();
                            dataStream = response.GetResponseStream();
                            reader = new StreamReader(dataStream);

                            await reader.ReadToEndAsync();

                            reader.Close();
                            response.Close();

                            await LogWriteAsync("Synced " + dnsRecord + " to WAN IP " + wanip + " on " + DateTime.UtcNow);                      
                        }
                    }
                    else
                    {
                        await LogWriteAsync("Could not find the WAN IP address. No syncing will proceed. Please, check Internet connectivity. " + DateTime.UtcNow);
                    }
                }
                catch (Exception ex)
                {
                    await LogWriteAsync(ex.Message);
                }
            }
            else
            {
                await LogWriteAsync("Configuration file not found in this directory: " + path);
            }
        }

        protected async Task LogWriteAsync(string message)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "log.dat");

            if (File.Exists(path))
            {
                FileInfo fi = new FileInfo(path);

                if (fi.Length >= 1024 * 1024 * 10) // 10 MB max file size.
                {
                    try
                    {
                        File.Delete(path);
                        using (FileStream fs = File.Create(path)) { }
                        await File.AppendAllTextAsync(path, message + Environment.NewLine);
                    }
                    catch { }
                }
                else
                {
                    await File.AppendAllTextAsync(path, message + Environment.NewLine);
                }
            }
            else
            {
                try
                {
                    using (FileStream fs = File.Create(path)) { }

                    await File.AppendAllTextAsync(path, message + Environment.NewLine);
                }
                catch { }
            }
        }

        protected async Task<string> InfoWANIPAsync()
        {
            string ip = "N/A";
            List<string> ips = new List<string>();
            
            #region Get IP
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                string ipAddress = await wc.DownloadStringTaskAsync("http://ipinfo.io/ip");

                ips.Add(ipAddress.Trim());
            }
            catch { }            
            
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                ips.Add(wc.DownloadString("https://api4.my-ip.io/ip").Trim());
            }
            catch { }           
            
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                string ipAddress = await wc.DownloadStringTaskAsync("https://ip4.seeip.org");

                ips.Add(ipAddress.Trim());
            }
            catch { }

            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                string ipAddress = await wc.DownloadStringTaskAsync("http://ipv4bot.whatismyipaddress.com");

                ips.Add(ipAddress.Trim());
            }
            catch { }
     
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                string ipAddress = await wc.DownloadStringTaskAsync("https://api.ipify.org");

                ips.Add(ipAddress.Trim());
            }
            catch { }      
            
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                string ipAddress = await wc.DownloadStringTaskAsync("https://checkip.amazonaws.com");

                ips.Add(ipAddress.Trim());
            }
            catch { }

            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.
                string ipAddress = await wc.DownloadStringTaskAsync("https://v4.ident.me");

                ips.Add(ipAddress.Trim());
            }
            catch { }
       
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy(); // remove any local proxies, direct connection.

                string dyndns = await wc.DownloadStringTaskAsync("http://checkip.dyndns.org");
                dyndns = dyndns.Replace("<html><head><title>Current IP Check</title></head><body>Current IP Address:", String.Empty);
                dyndns = dyndns.Replace("</body></html>", String.Empty);

                ips.Add(dyndns.Trim());
            }
            catch { }
            #endregion          

            try
            {
                ip = ips.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
            }
            catch { }

            return ip;
        }
        #endregion
    }
}