using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using GSeries;
using GSeries.Services;
using GSeries.Services.BitTorrent;
using System.Collections.Specialized;
using GSeries.External.DictionaryService;

namespace GSeries.Web.Controllers {
  [ExceptionHandler]
  [LogRequest]
  public class BitTorrentController : Controller {

    #region Fields
    static readonly IDictionary _log_props =
      Logger.PrepareLoggerProperties(typeof(BitTorrentController));
    IBitTorrentService _service; 
    #endregion

    public BitTorrentController(IBitTorrentService service) {
      _service = service;
    }

    /// <summary>
    /// Returns the BitTorrent service information.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns></returns>
    public ActionResult Info() {
      return Content(XmlUtil.ToXml<BitTorrentServiceInfo>(_service.GetServiceInfo(
        null)));
    }

    public ActionResult Index(string nameSpace, string name) {
      if (!string.IsNullOrEmpty(Request.Params["path"])) {
        // This is allowed for both GET and POST.
        return PublishInternal(nameSpace, name);
      }

      string verb = Request.HttpMethod;
      switch (verb.ToUpper()) {
        case "GET":
          return Get(nameSpace, name);
        case "POST":
          return PublishInternal(nameSpace, name);
        default:
          return new EmptyResult();
      }
    }

    [AcceptVerbs("POST")]
    public ActionResult Publish(string nameSpace, string name) {
      return PublishInternal(nameSpace, name);
    }

    /// <summary>
    /// Gets the data. It includes peeking, downloading entire data and data chunk.
    /// </summary>
    /// <param name="nameSpace">The name space.</param>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    [AcceptVerbs("GET")]
    public ActionResult Get(string nameSpace, string name) {
      string offset = Request.Params["offset"];
      string bytesToRead = Request.Params["bytesToRead"];
      string peek = Request.Params["peek"];

      try {
        if (!string.IsNullOrEmpty(peek) && Boolean.Parse(peek)) {
          // If peek == false, go to the next if condition.
          DataMetaInfo meta = _service.Peek(nameSpace, name);
          var xmlString = XmlUtil.ToXml<DataMetaInfo>(meta);
          return Content(xmlString);
        } else if (string.IsNullOrEmpty(offset) && string.IsNullOrEmpty(bytesToRead)) {
          DataMetaInfo meta = _service.Get(nameSpace, name);
          var xmlString = XmlUtil.ToXml<DataMetaInfo>(meta);
          return Content(xmlString);
        } else if (!string.IsNullOrEmpty(bytesToRead) &&
          !string.IsNullOrEmpty(bytesToRead)) {
          // Have both needed arguments
          var dataBlock =
            _service.Get(nameSpace, name, Int64.Parse(offset), Int32.Parse(bytesToRead));
          return File(dataBlock, HttpUtil.OctetStreamContentType,
            string.Format("{0}.{1}.{2}", name, offset, bytesToRead));
        } else {
          var toThrow = new HttpException((int)HttpStatusCode.BadRequest,
            "Unclear whether to download piece or whole data.");
          Util.LogBeforeThrow(toThrow, _log_props);
          throw toThrow;
        }
      } catch (DictionaryKeyNotFoundException ex) {
        var toThrow = new HttpException((int)HttpStatusCode.NotFound,
          "No file/directory available at this key.", ex);
        Util.LogBeforeThrow(toThrow, _log_props);
        throw toThrow;
      } catch (DictionaryServiceException ex) {
        var toThrow = new HttpException((int)HttpStatusCode.ServiceUnavailable,
          "Unable to get.", ex);
        Util.LogBeforeThrow(toThrow, _log_props);
        throw toThrow;
      }
    }

    private ActionResult PublishInternal(string nameSpace, string name) {
      try {
        var path = Request.Params["path"];
        var publisher = Publisher.CreatePublisher(Request.Params, this);
        if (!string.IsNullOrEmpty(path)) {
          // If the client quotes the path, we trim the quotation marks.
          path = path.Trim(new char[] { '"' });
          publisher.Execute(path);
        } else {
          publisher.Execute(nameSpace, name);
        }
      } catch (DictionaryKeyException ex) {
        var toThrow = new HttpException((int)HttpStatusCode.BadRequest,
          "The same key already exists. Change the name.", ex);
        Util.LogBeforeThrow(toThrow, _log_props);
        throw toThrow;
      } catch (DictionaryServiceException ex) {
        var toThrow = new HttpException((int)HttpStatusCode.ServiceUnavailable,
          "Unable to publish.", ex);
        Util.LogBeforeThrow(toThrow, _log_props);
        throw toThrow;
      }
      return new EmptyResult();
    }

    class Publisher {
      protected BitTorrentController _parent;

      internal static Publisher CreatePublisher(NameValueCollection parameters,
        BitTorrentController parent) {
        if ("update".Equals(parameters["action"], StringComparison.OrdinalIgnoreCase)) {
          return new Updater(parent);
        } else {
          return new Publisher(parent);
        }
      }

      protected internal Publisher(BitTorrentController parent) {
        _parent = parent;
      }

      internal protected virtual void Execute(string nameSpace, string name) {
        _parent._service.Publish(nameSpace, name);
      }

      internal protected virtual void Execute(string path) {
        _parent._service.Publish(path);
      }
    }

    class Updater : Publisher {
      protected internal Updater(BitTorrentController parent) : base(parent) { }

      internal protected override void Execute(string nameSpace, string name) {
        _parent._service.Update(nameSpace, name);
      }

      internal protected override void Execute(string path) {
        _parent._service.Update(path);
      }
    }
  }
}
