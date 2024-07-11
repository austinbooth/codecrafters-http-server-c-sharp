using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

// Uncomment this block to pass the first stage
TcpListener server = new TcpListener(IPAddress.Any, 4221);
server.Start();
var clientSocket = server.AcceptSocket(); // wait for client
Console.WriteLine("Client connected!");

var requestBuffer = new byte[1024];
var requestLength = clientSocket.Receive(requestBuffer);
var requestString = Encoding.ASCII.GetString(requestBuffer, 0, requestLength);
Console.WriteLine("Request:");
Console.WriteLine(requestString);
var requestLines = requestString.Split("\r\n");
var urlPath = requestLines[0].Split(' ')[1];

var responseString200NoContent = new ResponseBuilder()
    .WithResponseCode(200)
    .Build();
var responseString404NoContent = new ResponseBuilder()
    .WithResponseCode(404)
    .Build();

var responseString = urlPath == "/" ? responseString200NoContent : responseString404NoContent;

var sendBytes = Encoding.ASCII.GetBytes(responseString);
clientSocket.Send(sendBytes);
Console.WriteLine("Message Sent /> : " + responseString);


public class HttpResponse
{
    public const string HttpVersion = "HTTP/1.1";
    public string ResponseCodeAndDescription { get; set; } = string.Empty;
    public List<string> HeadersList { get; set; } = new();
    public string? Body { get; set; }
}

public class ResponseBuilder
{
    private readonly HttpResponse _httpResponse = new();

    private static readonly Dictionary<int, string> StatusCodes = new()
    {
        { 200, "200 OK" },
        { 404, "404 Not Found" }
    };

    public ResponseBuilder WithResponseCode(int code)
    {
        if (!StatusCodes.TryGetValue(code, out var description))
        {
            throw new ArgumentException("Unsupported HTTP status code, use 200 or 404");
        }
        _httpResponse.ResponseCodeAndDescription = description;
        return this;
    }

    public ResponseBuilder WithBody(string body)
    {
        _httpResponse.Body = body;
        return this;
    }

    public string Build()
    {
        AddContentHeaders();
        return BuildResponseString();
    }

    private string BuildResponseString() => String.Join("\r\n", new List<string?>
    {
        $"{HttpResponse.HttpVersion} {_httpResponse.ResponseCodeAndDescription}",
        String.Join("\r\n", _httpResponse.HeadersList),
        _httpResponse.HeadersList.Count == 0 ? null : "",
        _httpResponse.Body ?? ""
    }.Where(x => x != null));

    private void AddContentHeaders()
    {
        if (!string.IsNullOrEmpty(_httpResponse.Body))
        {
            _httpResponse.HeadersList.Add("Content-Type: text/plain");
            _httpResponse.HeadersList.Add($"Content-Length: {_httpResponse.Body.Length}");
        }
    }
}
