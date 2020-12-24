# CloudFlare Sync in .NET 5 on GNU/Linux

This is a code base for CloudFlare dynamic DNS records update. It permits you to synchronize finite number of DNS records over CloudFlare's API.
The code is tested only with A records, theoretically should work with any other DNS record types.


Read this before reusing the code:


**Rate limiting**

The Cloudflare API sets a maximum of 1,200 requests in a five minute period. Please, consider this limitation or you might get banned.


**Config.dat file**

This file contains the main settings

	* SyncIntervalInMilliseconds - Would set how frequently all domain names would be synced with CloudFlare. Please, note that all domain names are synced simultaneously.
							     If this value is changed the change would not be picked-up automatically. Please, restart the service or the executable. Other observation is
								 the minimum sync time which needs to consider time for WAN IP address check with the following formula total sync time = sync interval + approx. 10 seconds
								 WAN IP resolutions delay or 15 seconds sync time = 5 seconds + 10 seconds

	
	* Domains - This section contains finite number of subsections each of them holding the settings per particular domain. If you need to add extra domain for dynamic update, just add another 
				subsection with its corresponding values. The domain sections are picked-up automatically, no need for service restart.


	* New domain keys:
	    *  APIKey - Secret hash value from CloudFlare from your user profile.
	    *  Email - Email address used in CloudFlare for domain administration.
		*  Domain_Name - The top level domain name to work with (Example: mydomain.com)
		*  DNS_Record - DNS record to be updated (Example: dyn.mydomain.com or mail.mydomain.com etc. or the top level domain record could work as well Example: mydomain.com).
		*  Type - The DNS record type to be updated. Example: A, MX, CNAME etc. (Only A records updates are fully tested!)
		*  TTL - Time to live value in seconds. Please, consider that the free CloudFlare accounts are limited to minimum allowed ttl of 120 seconds / 2 min. Do not set values less than that.
		*  Proxied - Accepts true or false values and turns the CloudFlare proxy on or off. Please, do not apply to MX records.

**Log.dat file**

This file contains synchronization events notifications. This file will be automatically maintained to keep its size under 10 MB. After reaching 10 MB size, would be deleted and recreated.

	* Event types:
		* Success message per domain in UTC time: "Synced mydomain.com to WAN IP 123.123.123.0 on 12.12.2020 01:05:33 PM"
		* Failed WAN IP resolution message: "Could not find the WAN IP address. No syncing will proceed. Please, check Internet connectivity. 12.12.2020 01:05:33 PM"
		* Failed with no configuration file found message: "Configuration file not found in this directory: C:\config.dat"
		* General exception messages from try catch blocks.


**How to**

To get your APIKey please, logon to your CloudFlare account, go to "My Profile" > "API Tokens" > "API Keys" click on "View" button, type your password and resolve the Captcha. Copy the API Key value and use it into APIKey domain settings.

**Run as GNU/Linux service**

sudo vi /etc/systemd/system/CloudFlare.service


> [Unit]
> Description=CloudFlare Domain Sync
> 
> [Service]
> WorkingDirectory=/root/cloudflaresync
> ExecStart=/root/cloudflaresync/cloudflare
> SyslogIdentifier=root
> User=root
> 
> Restart=always
> RestartSec=5
> 
> KillSignal=SIGINT
> Environment=ASPNETCORE_ENVIRONMENT=Production
> Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
> 
> [Install]
> WantedBy=multi-user.target


**Enable service on machine start**
systemctl enable CloudFlare

**Start the service**
systemctl start CloudFlare

**Check that everything is OK.**
systemctl status CloudFlare

