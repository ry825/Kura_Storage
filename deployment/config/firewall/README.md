# KuraStorage nftables policy

The rendered table limits the ZeroTier interface to the KuraStorage HTTPS
listener and blocks forwarding in both directions. The LAN listener accepts
HTTPS only from the configured LAN CIDR. Existing host firewall policy remains
outside this table and must still protect management access.
