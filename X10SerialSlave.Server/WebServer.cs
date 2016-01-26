using HA4IoT.Networking;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Data.Json;

namespace X10SerialSlave.Server
{
    public sealed class WebServer
    {
        private BackgroundTaskDeferral _deferral;
        private HttpServer _httpServer;
        private IHttpRequestController _apiController;
        private readonly IX10Controller _x10Controller;

        public WebServer(IX10Controller x10Controller)
        {
            _x10Controller = x10Controller;
        }

        public void RunAsync(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();
            _x10Controller.Initialize();
            Task.Factory.StartNew(this.InitializeHttpApi, TaskCreationOptions.LongRunning);
        }

        public void InitializeHttpApi()
        {
            _httpServer = new HttpServer();
            HttpRequestDispatcher httpRequestDispatcher = new HttpRequestDispatcher(_httpServer);
            _apiController = httpRequestDispatcher.GetController("api");
            _apiController.Handle(HttpMethod.Get, "read").Using(HandleApiRead);
            _apiController.Handle(HttpMethod.Post, "write").Using(HandleApiWrite);
            _httpServer.StartAsync(80).Wait();
        }

        private void HandleApiRead(HttpContext httpContext)
        {
            JsonObject activity = new JsonObject();
            byte[] bytes = _x10Controller.GetBytes();
            activity.SetNamedValue("data", JsonValue.CreateStringValue(Encoding.ASCII.GetString(bytes)));
            httpContext.Response.Body = new JsonBody(activity);
        }

        private void HandleApiWrite(HttpContext httpContext)
        {
            JsonObject requestData;
            if (!JsonObject.TryParse(httpContext.Request.Body, out requestData))
            {
                httpContext.Response.StatusCode = HttpStatusCode.BadRequest;
                return;
            }
            JsonObject response = new JsonObject();
            byte[] message = new byte[3];
            message[0] = byte.Parse(requestData.GetNamedString("house"));
            message[1] = byte.Parse(requestData.GetNamedString("unit"));
            message[2] = byte.Parse(requestData.GetNamedString("command"));
            _x10Controller.WriteBytes(message);
            httpContext.Response.Body = new JsonBody(response);
        }
    }
}
