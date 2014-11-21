
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Routing;
using System.Web.UI;

using Newtonsoft.Json.Linq;

namespace TypeProvider.Service.Models
{
    public class DocumentationFilter : ActionFilterAttribute
    {
        private Dictionary<string, JObject> controllerLooker;

        public DocumentationFilter()
        {
            this.controllerLooker = new Dictionary<string, JObject>();

            var homeRoot = this.BuildHomeRoot();

            var homeId = this.BuildHomeId();

            controllerLooker.Add(homeRoot.Key, homeRoot.Description);
            controllerLooker.Add(homeId.Key, homeId.Description);
        }

        private dynamic BuildHomeId()
        {
            var HomeController = new JObject();

            var getObj = this.BuildVerbRepresentation("GET", "string");
            var postObj = this.BuildVerbRepresentation("PUT", "Void", "string");
            HomeController.Add("path", "/api/Values/%id:string%");
            HomeController.Add("verbs", new JArray(getObj, postObj));
            HomeController.Add("children", new JArray());
            
            var doc = new { Key = "/api/Values/id", Description = HomeController };
            return doc;
        }


        private dynamic BuildHomeRoot()
        {
            var HomeController = new JObject();

            var getObj = this.BuildVerbRepresentation("GET", "IEnumerable<string>");
            var postObj = this.BuildVerbRepresentation("POST", "Void", "string");
            var childObj = this.BuildChildren("/id/");
            HomeController.Add("verbs", new JArray(getObj, postObj));
            HomeController.Add("path", "/api/Values");
            HomeController.Add("children", childObj);

            var doc = new { Key = "/api/Values", Description = HomeController };
            return doc;
        }

        private JObject BuildVerbRepresentation(string verb, string responseType, string argType = null)
        {
            var verbj = new JObject();
            verbj.Add("verb", verb);
            verbj.Add("response", responseType);

            if (argType != null)
            {
                verbj.Add("body", argType);
            }

            return verbj;
        }

        private JArray BuildChildren(params string[] children)
        {
            return new JArray(children);
        }

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext.Request.Headers.Accept.Contains(new MediaTypeWithQualityHeaderValue("documentation/json")))
            {
                actionContext.Response = BuildResponse(actionContext.ActionDescriptor);
            }
            else
            {
                base.OnActionExecuting(actionContext);
            }
        }

        private HttpResponseMessage BuildResponse(HttpActionDescriptor actionDescriptor)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            var baseRoute = "/api/" + actionDescriptor.ControllerDescriptor.ControllerName;

            foreach (var p in actionDescriptor.ActionBinding.ParameterBindings)
            {
                baseRoute += "/" + p.Descriptor.ParameterName;
            }

            var json = new JObject();

            if (controllerLooker.ContainsKey(baseRoute))
            {
                json = controllerLooker[baseRoute];
            }
            else
            {
                json.Add("error", "unknown path");
            }

            response.Content = new StringContent(json.ToString());

            return response;
        }
    }
}