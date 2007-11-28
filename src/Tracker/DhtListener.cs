using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using System.Net;
using MonoTorrent.Tracker;
using Ipop;
using FuseSolution.Common;

namespace FuseSolution.Tracker {
  class DhtListener : ListenerBase {
    #region Fields

    private IPEndPoint endpoint;
    private System.Net.HttpListener listener;
    private DhtServiceProxy _proxy;

    #endregion Fields

    #region Properties

    /// <summary>
    /// True if the listener is waiting for incoming connections
    /// </summary>
    public override bool Running {
      get { return listener.IsListening; }
    }

    #endregion Properties


    #region Constructors

    public DhtListener(IPAddress address, ushort port)
      : this(new IPEndPoint(address, port), DhtType.BrunetDht) {
    }

    /**
     * @param endpoint The EndPoint that the tracker listens to
     */
    public DhtListener(IPEndPoint endpoint)
      : this(endpoint, DhtType.BrunetDht) {
    }


    public DhtListener(IPEndPoint endpoint, DhtType dhtType) {
      if (endpoint == null)
        throw new ArgumentNullException("endpoint");

      listener = new System.Net.HttpListener();
      this.endpoint = endpoint;
      _proxy = DhtServiceLocator.GetDhtServiceProxy(dhtType);
    }


    #endregion Constructors


    #region Methods

    /// <summary>
    /// Starts listening for incoming connections
    /// </summary>
    public override void Start() {
      listener.Prefixes.Add(string.Format("http://{0}:{1}/", endpoint.Address.ToString(), endpoint.Port));
      listener.Start();
      listener.BeginGetContext(EndGetRequest, null);
    }

    /// <summary>
    /// Stops listening for incoming connections
    /// </summary>
    public override void Stop() {
      listener.Stop();
    }

    /**
     * 
     */
    private void EndGetRequest(IAsyncResult result) {
      HttpListenerContext context;
      context = listener.EndGetContext(result);
      HandleRequest(context);
      context.Response.Close();
      Console.WriteLine(string.Format("Reponse sent to {0}, BeginGetContext again", context.Request.RemoteEndPoint.ToString()));
      listener.BeginGetContext(EndGetRequest, null);
    }

    /**
     * 
     */
    private void HandleRequest(HttpListenerContext context) {
      Console.WriteLine(string.Format("Request received from {0}. RawUrl={1}", 
          context.Request.RemoteEndPoint.ToString(), context.Request.RawUrl));
      RequestParameters parameters;
      bool isScrape = context.Request.RawUrl.StartsWith("/scrape", StringComparison.OrdinalIgnoreCase);
      NameValueCollection collection = ParseQuery(context.Request.RawUrl);
      Console.WriteLine(string.Format("Request Type: {0}", isScrape ? "scrape" : "announce"));
      if (isScrape) {
        parameters = new ScrapeParameters(collection, context.Request.RemoteEndPoint.Address);
      }
      else
        parameters = new AnnounceParameters(collection, context.Request.RemoteEndPoint.Address);

      if (!parameters.IsValid) {
        // The failure reason has already been filled in to the response
        return;
      } else {
        if (isScrape)
          RaiseScrapeReceived((ScrapeParameters)parameters);
        else
          //RaiseAnnounceReceived((AnnounceParameters)parameters);
          HandleAnnounceRequest((AnnounceParameters)parameters);
      }

      byte[] response = parameters.Response.Encode();
      Console.Write(string.Format("Reponse built: {0} (Base32)", Brunet.Base32.Encode(response)));
      context.Response.ContentType = "text/plain";
      context.Response.StatusCode = 200;
      context.Response.OutputStream.Write(response, 0, response.Length);
    }

    private NameValueCollection ParseQuery(string url) {
      url = url.Substring(url.IndexOf('?') + 1);
      string[] parts = url.Split('&', '=');
      NameValueCollection c = new NameValueCollection(1 + parts.Length / 2);
      for (int i = 0; i < parts.Length; i += 2)
        if (parts.Length > i + 1)
          c.Add(parts[i], parts[i + 1]);

      return c;
    }

    /**
     * Get the peers from Dht and generate AnnounceParameters of a list of peers
     * Put the peer info of this announcing peer to Dht
     * @param parameters AnnounceParameters from the requesting client. Is modified in method.
     */
    private void HandleAnnounceRequest(AnnounceParameters parameters) {
      ICollection<PeerEntry> entries = _proxy.GetPeers(parameters.InfoHash);
      Console.WriteLine(string.Format("##{0}##", entries.Count));
      foreach (PeerEntry entry in entries) {
        AnnounceParameters par = GenerateAnnounceParameters(parameters.InfoHash, entry);
        if (par.IsValid) {
          //Tracker will write to the par.Reponse but we don't use it
          Console.WriteLine("!!!!!!!!!!!!!!!!!!");
          RaiseAnnounceReceived(par);
        } else {
          Console.WriteLine("Parameters invalid!");
        }
        Console.WriteLine(string.Format("Tracker's reponse for this peer from DHT: {0}", par.Response.ToString()));
      }
      //Got all I need, now announce myself
      _proxy.AnnouncePeer(parameters.InfoHash, parameters);
      RaiseAnnounceReceived(parameters);
    }

    /**
     * Generate the AnnounceParameters from PeerEntry
     */
    private AnnounceParameters GenerateAnnounceParameters(byte[] infoHash, PeerEntry entry) {
      NameValueCollection c = new NameValueCollection();
      //"info_hash", "peer_id", "port", "uploaded(bytes)", "downloaded", "left", "compact"
      //infoHash here should be just like what's in HttpRequests
      c.Add("info_hash", System.Web.HttpUtility.UrlEncode(infoHash));
      c.Add("peer_id", entry.PeerID);
      c.Add("port", entry.PeerPort.ToString());
      //fake the mandatory fields, these are solely used to compute the upload/download speed
      c.Add("uploaded", "0");
      c.Add("downloaded", "0");
      c.Add("left", "1000");
      c.Add("compact", "1");
      //optional but need to set
      if (entry.PeerState != MonoTorrent.Common.TorrentEvent.None) {
        c.Add("event", entry.PeerEventAsRequestKey);
      }
      //NOTE: "ip" is optional and it won't be put in here, we set the announce's clientAddress instead
      AnnounceParameters par = new AnnounceParameters(c, IPAddress.Parse(entry.PeerIP));
      return par;
    }

    #endregion Methods
  }
}
