namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    private async Task<int> GenInbounds(V2rayConfig v2rayConfig)
    {
        try
        {
            var listen = "0.0.0.0";
            v2rayConfig.inbounds = [];

            var inbound = GetInbound(_config.Inbound.First(), EInboundProtocol.socks, true);
            v2rayConfig.inbounds.Add(inbound);

            // Add mixed protocol inbound for HTTP proxy support
            var inboundMixed = GetInbound(_config.Inbound.First(), EInboundProtocol.mixed, false);
            inboundMixed.protocol = EInboundProtocol.mixed.ToString();
            v2rayConfig.inbounds.Add(inboundMixed);

            if (_config.Inbound.First().SecondLocalPortEnabled)
            {
                var inbound2 = GetInbound(_config.Inbound.First(), EInboundProtocol.socks2, true);
                v2rayConfig.inbounds.Add(inbound2);
            }

            if (_config.Inbound.First().AllowLANConn)
            {
                if (_config.Inbound.First().NewPort4LAN)
                {
                    var inbound3 = GetInbound(_config.Inbound.First(), EInboundProtocol.socks3, true);
                    inbound3.listen = listen;
                    v2rayConfig.inbounds.Add(inbound3);

                    //auth
                    if (_config.Inbound.First().User.IsNotEmpty() && _config.Inbound.First().Pass.IsNotEmpty())
                    {
                        inbound3.settings.auth = "password";
                        inbound3.settings.accounts = new List<AccountsItem4Ray> { new AccountsItem4Ray() { user = _config.Inbound.First().User, pass = _config.Inbound.First().Pass } };
                    }
                }
                else
                {
                    inbound.listen = listen;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return await Task.FromResult(0);
    }

    private Inbounds4Ray GetInbound(InItem inItem, EInboundProtocol protocol, bool bSocks)
    {
        string result = EmbedUtils.GetEmbedText(Global.V2raySampleInbound);
        if (result.IsNullOrEmpty())
        {
            return new();
        }

        var inbound = JsonUtils.Deserialize<Inbounds4Ray>(result);
        if (inbound == null)
        {
            return new();
        }
        inbound.tag = protocol.ToString();
        
        // Custom port calculation to avoid conflicts with other apps
        // SOCKS uses LocalPort (10820), mixed uses LocalPort + 6 (10826)
        switch (protocol)
        {
            case EInboundProtocol.socks:
                inbound.port = inItem.LocalPort; // 10820
                break;
            case EInboundProtocol.mixed:
                inbound.port = inItem.LocalPort + 6; // 10826
                break;
            case EInboundProtocol.socks2:
                inbound.port = inItem.LocalPort + 1; // 10821
                break;
            case EInboundProtocol.socks3:
                inbound.port = inItem.LocalPort + 2; // 10822
                break;
            case EInboundProtocol.pac:
                inbound.port = inItem.LocalPort + 3; // 10823
                break;
            default:
                inbound.port = inItem.LocalPort + (int)protocol;
                break;
        }
        
        // Use SOCKS for SOCKS protocols, otherwise use the actual protocol
        if (bSocks || protocol == EInboundProtocol.socks || protocol == EInboundProtocol.socks2 || protocol == EInboundProtocol.socks3)
        {
            inbound.protocol = EInboundProtocol.mixed.ToString(); // mixed supports both SOCKS and HTTP
        }
        else
        {
            inbound.protocol = protocol.ToString();
        }
        
        inbound.settings.udp = inItem.UdpEnabled;
        inbound.sniffing.enabled = inItem.SniffingEnabled;
        inbound.sniffing.destOverride = inItem.DestOverride;
        inbound.sniffing.routeOnly = inItem.RouteOnly;

        return inbound;
    }
}
