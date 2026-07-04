namespace RPA.Infrastructure.Tests;

using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Http;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow.Activities.Api;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

public class ApiHttpActivityTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeHttpFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class FakeCtx : IActivityExecutionContext
    {
        private readonly Dictionary<string, object> _vars;
        private readonly Dictionary<string, string> _creds;

        public FakeCtx(Dictionary<string, object> vars, Dictionary<string, string> creds = null)
        {
            _vars = vars;
            _creds = creds ?? new();
        }

        public T GetVariable<T>(string name)
        {
            if (_vars.TryGetValue(name, out var v) && v is T t) return t;
            return default;
        }

        public void SetVariable(string name, object value) { }
        public Task<string> GetCredentialAsync(string name) => Task.FromResult(_creds.TryGetValue(name, out var v) ? v : "");
        public Task<string> GetAssetAsync(string name) => Task.FromResult<string>(null);
        public void Log(string msg, RPA.Domain.Interfaces.LogLevel level = RPA.Domain.Interfaces.LogLevel.Information) { }
        public string TimeZone => "UTC";
        public Guid JobRunId => Guid.NewGuid();
    }

    private static ApiHttpActivity Create(HttpMessageHandler h) => new(new FakeHttpFactory(h));

    [Fact]
    public async Task Get_ReturnsStatus200AndBody()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("test") });
        var a = Create(h);
        var c = new FakeCtx(new() { ["url"] = "https://test.test/api" });

        var r = await a.ExecuteAsync(c);

        Assert.Equal(200, r["statusCode"]);
    }

    [Fact]
    public async Task Bearer_SetsAuthorizationHeader()
    {
        var requests = new List<HttpRequestMessage>();
        var h = new FakeHandler(req =>
        {
            requests.Add(req);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        var a = Create(h);
        var c = new FakeCtx(new() { ["url"] = "https://test.test/api", ["authType"] = "Bearer", ["credentialName"] = "tok" }, new() { ["tok"] = "secret" });

        await a.ExecuteAsync(c);

        Assert.Equal("Bearer", requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("secret", requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Basic_EncodesBase64()
    {
        var requests = new List<HttpRequestMessage>();
        var h = new FakeHandler(req =>
        {
            requests.Add(req);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        var a = Create(h);
        var c = new FakeCtx(new() { ["url"] = "https://test.test/api", ["authType"] = "Basic", ["credentialName"] = "u:p" }, new() { ["u:p"] = "user:pass" });

        await a.ExecuteAsync(c);

        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass"));
        Assert.Equal("Basic", requests[0].Headers.Authorization?.Scheme);
        Assert.Equal(expected, requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Status400_ThrowsBusiness()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var a = Create(h);
        var c = new FakeCtx(new() { ["url"] = "https://test.test/api" });

        await Assert.ThrowsAsync<BusinessException>(() => a.ExecuteAsync(c));
    }

    [Fact]
    public async Task Status500_ThrowsSystem()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var a = Create(h);
        var c = new FakeCtx(new() { ["url"] = "https://test.test/api" });

        await Assert.ThrowsAsync<SystemException>(() => a.ExecuteAsync(c));
    }
}
