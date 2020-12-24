# CloudFlare Sync in .NET 5 on GNU/Linux

This is a code base for CloudFlare dynamic DNS records update. It permits you to synchronize finite number of DNS records over CloudFlare's API.
The code is tested only with A records, theoretically should work with any other DNS record types.


Read this before reusing the code:


* Rate limiting
	* The Cloudflare API sets a maximum of 1,200 requests in a five minute period. Please, consider this limitation or you might get banned.


* Config.dat file
	* This file contains the main service settings:
	
	SyncIntervalInMilliseconds - Would set how frequently all domain names would be synced with CloudFlare. Please, note that all domain names are synced simultaneously.
							     If this value is changed the change would not be picked-up automatically. Please, restart the service or the executable. Other observation is
								 the minimum sync time which needs to conside time for WAN IP address check with the following formula total synctime = sync interval + aprox. 10 seconds
								 WAN IP resolutions delay or 15 seconds sync time = 5 seconds + 10 seconds
	
	Domains - This section contains finite number of subsections each of them holding the settings per particular domain. If you need to add extra domain for dynamic update, just add another
		      Domain subsection with it's corresponding values. The domain sections are picked-up automatically, no need for service restart.

	New domain keys:
	    Domains > APIKey - Secret hash value from CloudFlare in my profile.
	    Domains > Email - Email address used in CloudFlare for domain administration.
		Domains > Domain_Name - The top level domain name to work with (Example: mydomain.com)
		Domains > DNS_Record - DNS record to be updated (Example: dyn.mydomain.com mail.mydomain.com etc. or the top level domain record could work as well Example: mydomain.com).
		Domains > Type - The DNS record type to be updated. Example: A, MX, CNAME etc.
		Domains > TTL - Time to live value in seconds. Please, conside that the free CloudFlare accounts are limited to minimum of 120 sec = 2 min. Do not set values less than that.
		Domains > Proxied - Accepts true or false values and turns the CloudFlare proxy on or off. Please, do not apply to MX records.

**********************
	log.dat
**********************

This file contains synchronization events notifications. This file will be automatically maintained to keep it's size under 10 MB. After reaching 10 MB size, would be deleted and recreated.

	Event types:
		Success message per domain in UTC time: "Synced mydomain.com to WAN IP 123.123.123.0 on 12.12.2020 01:05:33 PM"
		Failed WAN IP resolution message: "Could not find the WAN IP address. No syncing will proceed. Please, check Internet connectivity. 12.12.2020 01:05:33 PM"
		Failed no configuration file found message: "Configuration file not found in this directory: C:\config.dat"
		General exception messages from try catch block.

**********************
	How to
**********************

To get your APIKey please, logon to your CloudFlare account, go to "My Profile" > "API Tokens" > "API Keys" click on "View" button, type your password and resolve the Captcha. Copy the API Key
value and use it into APIKey domain settings.


******************************
       Add linux service
******************************

vi /etc/systemd/system/CloudFlare.service


[Unit]
Description=CloudFlare Domain Sync

[Service]
# will set the Current Working Directory (CWD)
WorkingDirectory=/root/cloudflaresync
ExecStart=/root/cloudflaresync/cloudflare
SyslogIdentifier=root
User=root

Restart=always
RestartSec=5

KillSignal=SIGINT
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target

systemctl enable CloudFlare
systemctl start CloudFlare
systemctl status CloudFlare
