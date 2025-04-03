using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api;

[SignalRConnection]
public class Functions : ServerlessHub<IChatClient>
{
    private const string HubName = nameof(Functions);
    private readonly ILogger _logger;
    private readonly IHostEnvironment _env;

    public Functions(IServiceProvider serviceProvider, ILogger<Functions> logger, IHostEnvironment env) : base(serviceProvider)
    {
        _logger = logger;
        _env = env;
    }

    [Function("index")]
    public async Task<HttpResponseData> GetWebPage([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequestData req, CancellationToken cancel)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        string indexPath = Path.Combine(_env.ContentRootPath, "wwwroot", "index.html");
        string indexContent = await File.ReadAllTextAsync(indexPath, cancel);
        await response.WriteStringAsync(indexContent, cancel);
        response.Headers.Add("Content-Type", "text/html");
        return response;
    }

    [Function("negotiate")]
    public async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req, CancellationToken cancel)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        var negotiateResponse = await NegotiateAsync(
            new NegotiationOptions { UserId = req.Headers.GetValues("userId").FirstOrDefault() });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray(), cancel);
        return response;
    }

    [Function("OnConnected")]
    public async Task OnConnected(
        [SignalRTrigger(HubName, "connections", "connected")] SignalRInvocationContext invocationContext)
    {
        invocationContext.Headers.TryGetValue("Authorization", out var auth);
        if (string.IsNullOrEmpty(auth)) return;
        _logger.LogInformation($"{invocationContext.ConnectionId} has connected");
        await Clients.All.newConnection(new NewConnection(invocationContext.ConnectionId, auth));
    }

    [Function("Broadcast")]
    public Task Broadcast(
        [SignalRTrigger(HubName, "messages", "broadcast", "message")]
        SignalRInvocationContext invocationContext,
        string message)
    {
        return Clients.All.newMessage(new NewMessage(invocationContext, message));
    }

    [Function("SendToGroup")]
    public Task SendToGroup(
        [SignalRTrigger(HubName, "messages", "SendToGroup", "groupName", "message")]
        SignalRInvocationContext invocationContext,
        string groupName,
        string message)
    {
        return Clients.Group(groupName).newMessage(new NewMessage(invocationContext, message));
    }

    [Function("SendToUser")]
    public Task SendToUser(
        [SignalRTrigger(HubName, "messages", "SendToUser", "userName", "message")]
        SignalRInvocationContext invocationContext,
        string userName,
        string message)
    {
        return Clients.User(userName).newMessage(new NewMessage(invocationContext, message));
    }

    [Function("SendToConnection")]
    public Task SendToConnection(
        [SignalRTrigger(HubName, "messages", "SendToConnection", "connectionId", "message")]
        SignalRInvocationContext invocationContext,
        string connectionId,
        string message)
    {
        return Clients.Client(connectionId).newMessage(new NewMessage(invocationContext, message));
    }

    [Function("JoinGroup")]
    public Task JoinGroup(
        [SignalRTrigger(HubName, "messages", "JoinGroup", "connectionId", "groupName")]
        SignalRInvocationContext invocationContext,
        string connectionId,
        string groupName)
    {
        return Groups.AddToGroupAsync(connectionId, groupName);
    }

    [Function("LeaveGroup")]
    public Task LeaveGroup(
        [SignalRTrigger(HubName, "messages", "LeaveGroup", "connectionId", "groupName")]
        SignalRInvocationContext invocationContext,
        string connectionId,
        string groupName)
    {
        return Groups.RemoveFromGroupAsync(connectionId, groupName);
    }

    [Function("JoinUserToGroup")]
    public Task JoinUserToGroup(
        [SignalRTrigger(HubName, "messages", "JoinUserToGroup", "userName", "groupName")]
        SignalRInvocationContext invocationContext,
        string userName,
        string groupName)
    {
        return UserGroups.AddToGroupAsync(userName, groupName);
    }

    [Function("LeaveUserFromGroup")]
    public Task LeaveUserFromGroup(
        [SignalRTrigger(HubName, "messages", "LeaveUserFromGroup", "userName", "groupName")]
        SignalRInvocationContext invocationContext,
        string userName,
        string groupName)
    {
        return UserGroups.RemoveFromGroupAsync(userName, groupName);
    }

    [Function("OnDisconnected")]
    public void OnDisconnected(
        [SignalRTrigger(HubName, "connections", "disconnected")] SignalRInvocationContext invocationContext)
    {
        _logger.LogInformation($"{invocationContext.ConnectionId} has disconnected");
    }
}

public class NewMessage
{
    public string ConnectionId { get; }
    public string Sender { get; }
    public string Text { get; }

    public NewMessage(SignalRInvocationContext invocationContext, string message)
    {
        Sender = string.IsNullOrEmpty(invocationContext.UserId) ? string.Empty : invocationContext.UserId;
        ConnectionId = invocationContext.ConnectionId;
        Text = message;
    }
}

public class NewConnection
{
    public string ConnectionId { get; }

    public string Authentication { get; }

    public NewConnection(string connectionId, string auth)
    {
        ConnectionId = connectionId;
        Authentication = auth;
    }
}

public interface IChatClient
{
    Task newMessage(NewMessage message);
    Task newConnection(NewConnection connection);
}
