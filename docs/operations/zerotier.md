# ZeroTier operations boundary

ZeroTier is an externally managed network transport. KuraStorage does not join
networks, authorize members, store a Network ID, or revoke ZeroTier identities.

Before remote E2E validation, the administrator must use the external ZeroTier
controller to:

1. join the Raspberry Pi and Android device to the intended private network;
2. authorize only the expected members;
3. assign and record stable managed IP addresses;
4. verify that the configured ZeroTier CIDR contains both addresses;
5. remove authorization for retired or lost devices.

KuraStorage device revocation and ZeroTier member revocation are independent.
For a lost device, perform both. Membership alone never grants KuraStorage
authentication or file authorization.

The Raspberry Pi nftables policy permits the ZeroTier interface to reach only
the ZeroTier API IP on TCP 443 and drops forwarding to LAN. SSH, PostgreSQL,
and SMB must remain unreachable over that interface.
When UFW is already active, the installer mirrors only this ZeroTier HTTPS
allow rule into UFW; otherwise UFW's later drop-policy chain would override
the nftables accept path. Existing non-KuraStorage UFW rules remain untouched.
Validate these controls from an authorized ZeroTier member after every network
or firewall change.

## Private network trust model

Controller Flow Rules are not required for KuraStorage. The administrator must
instead operate the KuraStorage ZeroTier network as a private trust boundary:

1. authorize only the Raspberry Pi and trusted client devices that currently
   require KuraStorage access;
2. do not configure a managed route that exposes the household LAN through a
   ZeroTier member;
3. remove authorization immediately when a device is retired, lost, or no
   longer requires access;
4. review the authorized member list after adding a device and during regular
   maintenance;
5. keep KuraStorage authentication, TLS, and device revocation enabled even
   for authorized ZeroTier members.

The Raspberry Pi firewall still restricts traffic terminating on or forwarded
by the Pi: HTTPS on TCP 443 is allowed, while SSH, SMB, PostgreSQL, and LAN
forwarding are denied on the ZeroTier interface. Direct peer-to-peer traffic
between members does not pass through the Pi and is outside KuraStorage's
enforcement boundary. The administrator accepts and manages that risk by
keeping the private network limited to trusted members.

After every member, route, or firewall change, verify HTTPS to the Pi and
confirm that the Pi's ZeroTier address does not accept TCP 22, 445, or 5432.

## Android background operation

ZeroTier must remain available while KuraStorage is in the foreground or
background. On the Android device:

1. exclude ZeroTier One from battery optimization;
2. allow background activity and automatic launch when the device vendor
   provides those controls;
3. after a reboot, unlock the device once and confirm that the intended network
   is enabled and the VPN indicator or ZeroTier interface is present;
4. open KuraStorage and confirm that the connection is `REMOTE_SECURE` when the
   LAN route is unavailable;
5. repeat the check after leaving both apps in the background with the screen
   off.

If KuraStorage becomes unreachable, open ZeroTier One, reconnect the intended
network, and recheck the vendor battery and background-activity settings. Do
not enable an unrelated ZeroTier network during recovery.
