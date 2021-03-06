﻿/*
 * #########################################################################*/
/* #                                                                       #*/
/* #  This file is part of PGTNetworkMap project, which is written         #*/
/* #  as a PGT plug-in to perform Layer 3 network inventory.               #*/
/* #                                                                       #*/
/* #  You may not use this file except in compliance with the license.     #*/
/* #                                                                       #*/
/* #  Copyright Laszlo Frank (c) 2014-2018                                 #*/
/* #                                                                       #*/
/* #########################################################################*/

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace L3Discovery.ProtocolParsers.JunOS.STATIC
{
  public class Junos_STATICParser : IGenericProtocolParser, ISTATICProtocolParser
  {
    private string _OperationStatusLabel = "Idle";
    private IRouter _router;

    public string OperationStatusLabel => _OperationStatusLabel;

    public bool Initilize(IRouter router, Enum protocol)
    {
      _router = router;
      if (protocol is NeighborProtocol && (NeighborProtocol)protocol == NeighborProtocol.STATIC)
      {
        return router?.Vendor == "JunOS";
      }
      else return false;
    }

    public void Parse(INeighborRegistry registry, CancellationToken token)
    {
      if (_router?.Session == null || registry == null || !_router.Session.IsConnected()) throw new ArgumentException("Unable to parse STATIC routes, invalid parameters.");
      try
      {
        _OperationStatusLabel = "Querying static routes...";
        string routes = _router.Session.ExecCommand("show route protocol static");
        token.ThrowIfCancellationRequested();
        MatchCollection knownNetworks = Regex.Matches(routes, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b\/\d{1,2}", RegexOptions.Compiled);
        if (knownNetworks.Count > 0)
        {
          _OperationStatusLabel = "Processing static routes...";
          // insert actual routes
          for (int i = 0; i < knownNetworks.Count; i++)
          {
            string thisNetwork = knownNetworks[i].Value;
            int routeBlockStart = knownNetworks[i].Index;
            int routeBlockEnd = i == knownNetworks.Count - 1 ? routes.Length : knownNetworks[i + 1].Index;
            string thisRouteBlock = routes.Substring(routeBlockStart, routeBlockEnd - routeBlockStart);
            bool isBestRoute = thisRouteBlock.IndexOf("*[") > 0;
            MatchCollection protocolBlocksHeaders = Regex.Matches(thisRouteBlock, @"\[(.*?)\]", RegexOptions.Compiled);
            for (int j = 0; j < protocolBlocksHeaders.Count; j++)
            {
              try
              {
                string thisProtocolBlockHeader = protocolBlocksHeaders[j].Value;
                int protocolBlockStart = protocolBlocksHeaders[j].Index;
                int protocolBlockEnd = j == protocolBlocksHeaders.Count - 1 ? thisRouteBlock.Length : protocolBlocksHeaders[j + 1].Index;
                string thisProtocolBlock = thisRouteBlock.Substring(protocolBlockStart, protocolBlockEnd - protocolBlockStart);
                string thisProtocolName = Regex.Match(thisProtocolBlockHeader, @"[a-zA-Z]+", RegexOptions.Compiled)?.Value;
                string nextHopAddress = Regex.Match(thisProtocolBlock, @"(?<=to )[\d\.]{0,99}", RegexOptions.Compiled)?.Value;
                string nextHopViaInterfaceName = Regex.Match(thisProtocolBlock, @"(?<=via ).*", RegexOptions.Compiled)?.Value?.Trim('\r');

                _OperationStatusLabel = string.Format("Querying router interface {0}...", nextHopViaInterfaceName);
                RouterInterface ri = _router.GetInterfaceByName(nextHopViaInterfaceName);
                _OperationStatusLabel = string.Format("Registering STATIC neighbor {0}...", nextHopAddress);
                registry.RegisterSTATICNeighbor(_router, thisNetwork, nextHopAddress, ri.Address, ri);
              }
              catch (Exception Ex)
              {
                DebugEx.WriteLine("Jumos_STATICParser error : " + Ex.Message);
              }
            }
          }
        }
        _OperationStatusLabel = "JunOS STATIC route parser completed.";
      }
      catch (Exception Ex)
      {
        _OperationStatusLabel = "JunOS STATIC route parser failed with error : " + Ex.Message;
      }
    }

		public void Reset()
		{
			_router = null;
			_OperationStatusLabel = "Init";
		}

		public ISpecializedProtocolParser ProtocolDependentParser(Enum protocol)
    {
      if (protocol is NeighborProtocol && (NeighborProtocol)protocol == NeighborProtocol.STATIC) return this;
      else return null;
    }

    public Enum[] SupportedProtocols => new Enum[] { NeighborProtocol.STATIC };

    public string SupportTag => "Juniper, JunOS STATIC Protocol Parser module v2.0";
  }
}
