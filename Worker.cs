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
        public static readonly string configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.dat");
        public static readonly string logPath = Path.Combine(Directory.GetCurrentDirectory(), "log.dat");

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                string config = await File.ReadAllTextAsync(configPath);
                dynamic data = JObject.Parse(config);

                while (!stoppingToken.IsCancellationRequested)
                {
                    _ = Task.Run(() => SyncAllAsync());
                    await Task.Delay((int)data.SyncIntervalInMilliseconds, stoppingToken); // Time to wait between syncs. All domains are synced at once when this time expires.
                }
            }
            catch (Exception ex)
            {
                await LogWriteAsync("General exeception ExecuteAsync: " + ex.Message + " " + DateTime.UtcNow);
            }
        }
		
        #region Methods      
        protected async Task SyncAllAsync()
        {
            if (File.Exists(configPath))
            {
                string wanip = await InfoWANIPAsync();

                try
                {                                    
                    if(wanip != "N/A")
                    { 
                        string configuration = await File.ReadAllTextAsync(configPath);

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
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.169 Safari/537.36";
                            req.Timeout = 5000; // time spent trying to establish a connection (not including lookup time) before give up.
                            req.Accept = "*/*";
                            req.Method = "GET";
                            req.Headers.Add("X-Auth-Email:" + email);
                            req.Headers.Add("X-Auth-Key:" + apikey);
                            req.ContentType = "application/json";

                            HttpWebResponse resp = (HttpWebResponse) await req.GetResponseAsync();
                            Stream dataStream = resp.GetResponseStream();
                            StreamReader reader = new StreamReader(dataStream);

                            string zones = String.Empty;
                            dynamic zones_data = JObject.Parse(await reader.ReadToEndAsync());
                            dynamic results = zones_data.result;

                            reader.Close();
                            resp.Close();
                                
                            foreach (dynamic result in results)
                            {
                                zones = Convert.ToString(result.id);
                            }

                            // Get dns_record id for the DNS record, which is used in the update statement below.
                            req = (HttpWebRequest)WebRequest.Create("https://api.cloudflare.com/client/v4/zones/" + zones + "/dns_records?type=" + type + "&name=" + dnsRecord);
                            req.Proxy = null;
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.169 Safari/537.36";
                            req.Timeout = 5000; // time spent trying to establish a connection (not including lookup time) before give up.
                            req.Accept = "*/*";
                            req.Method = "GET";
                            req.Headers.Add("X-Auth-Email:" + email);
                            req.Headers.Add("X-Auth-Key:" + apikey);
                            req.ContentType = "application/json";

                            resp = (HttpWebResponse) await req.GetResponseAsync();
                            dataStream = resp.GetResponseStream();
                            reader = new StreamReader(dataStream);

                            string dns_records = String.Empty;
                            dynamic zone_data = JObject.Parse(await reader.ReadToEndAsync());
                            dynamic zone_results = zone_data.result;

                            reader.Close();
                            resp.Close();

                            foreach (dynamic result in zone_results)
                            {
                                dns_records = Convert.ToString(result.id);
                            }

                            // Send zone update.
                            req = (HttpWebRequest)WebRequest.Create("https://api.cloudflare.com/client/v4/zones/" + zones + "/dns_records/" + dns_records);
                            req.Proxy = null;
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.169 Safari/537.36";
                            req.Timeout = 5000; // time spent trying to establish a connection (not including lookup time) before give up.
                            req.Accept = "*/*";
                            req.Method = "PUT";
                            req.Headers.Clear();
                            req.Headers.Add("X-Auth-Email:" + email);
                            req.Headers.Add("X-Auth-Key:" + apikey);
                            req.ContentType = "application/json";

                            string jsonData = "{\"type\":\"" + type + "\",\"name\":\"" + dnsRecord + "\",\"content\":\"" + wanip + "\",\"ttl\":" + ttl + ",\"proxied\":" + proxied + "}";

                            StreamWriter sw = new StreamWriter(await req.GetRequestStreamAsync());
                            sw.Write(jsonData);
                            sw.Flush();
                            sw.Close();
                           
                            resp = (HttpWebResponse)await req.GetResponseAsync();
                            dataStream = resp.GetResponseStream();
                            reader = new StreamReader(dataStream);

                            await reader.ReadToEndAsync();

                            reader.Close();
                            resp.Close();

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
                    await LogWriteAsync("General exeception SyncAllAsync: " + ex.Message + " " + DateTime.UtcNow);
                }
            }
            else
            {
                await LogWriteAsync("Configuration file not found in this directory: " + configPath + " " + DateTime.UtcNow);
            }
        }

        protected async Task LogWriteAsync(string message)
        {
            if (File.Exists(logPath))
            {
                try
                {
                    FileInfo fi = new FileInfo(logPath);

                    if (fi.Length >= 1024 * 1024 * 10) // 10 MB max file size.
                    {
                        await File.WriteAllTextAsync(logPath, message + Environment.NewLine);
                    }
                    else
                    {
                        await File.AppendAllTextAsync(logPath, message + Environment.NewLine);
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    await File.WriteAllTextAsync(logPath, message + Environment.NewLine);
                }
                catch { }
            }
        }

        protected async Task<string> InfoWANIPAsync()
        {
            string ip = "N/A";
            List<string> ips = new List<string>();
            
            #region Get IP
            WebClient wc = new WebClient();
            wc.Proxy = null; // remove any local proxies, direct connection.

            string ipAddress = String.Empty;

            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("http://ipinfo.io/ip");
                ips.Add(ipAddress.Trim());
            }
            catch { }            
            
            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("https://api4.my-ip.io/ip");
                ips.Add(ipAddress.Trim());
            }
            catch { }           
            
            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("https://ip4.seeip.org");
                ips.Add(ipAddress.Trim());
            }
            catch { }

            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("http://ipv4bot.whatismyipaddress.com");
                ips.Add(ipAddress.Trim());
            }
            catch { }
     
            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("https://api.ipify.org");
                ips.Add(ipAddress.Trim());
            }
            catch { }      
            
            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("https://checkip.amazonaws.com");
                ips.Add(ipAddress.Trim());
            }
            catch { }

            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("https://v4.ident.me");
                ips.Add(ipAddress.Trim());
            }
            catch { }
       
            try
            {
                ipAddress = await wc.DownloadStringTaskAsync("http://checkip.dyndns.org");
                ipAddress = ipAddress.Replace("<html><head><title>Current IP Check</title></head><body>Current IP Address:", String.Empty);
                ipAddress = ipAddress.Replace("</body></html>", String.Empty);

                ips.Add(ipAddress.Trim());
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