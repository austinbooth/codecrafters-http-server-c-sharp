using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace codecrafters_http_server;

public class Program
{
    public static async Task Main(string[] args)
    {
        // You can use print statements as follows for debugging, they'll be visible when running tests.
        Console.WriteLine("Logs from your program will appear here!");

        TcpListener server = new TcpListener(IPAddress.Any, 4221);
        server.Start();

        while (true)
        {
            var clientSocket = await server.AcceptSocketAsync(); // wait for client
            Console.WriteLine("Client connected!");

            _ = HandleRequestAsync(clientSocket);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    static async Task HandleRequestAsync(Socket clientSocket)
    {
        await using NetworkStream networkStream = new NetworkStream(clientSocket);
        try
        {
            var requestBuffer = new byte[1024];
            var requestLength = await networkStream.ReadAsync(requestBuffer, 0, requestBuffer.Length);
            var requestString = Encoding.ASCII.GetString(requestBuffer, 0, requestLength);
            Console.WriteLine("Request:");
            Console.WriteLine(requestString);

            var responseString = await Router.GetResponse(requestString);

            var sendBytes = Encoding.ASCII.GetBytes(responseString);
            clientSocket.Send(sendBytes);
            Console.WriteLine("Message Sent /> : " + responseString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }
        finally
        {
            clientSocket.Close();
        }
    }
}

public class HttpResponse
{
    public const string HttpVersion = "HTTP/1.1";
    public string ResponseCodeAndDescription { get; set; } = string.Empty;
    public List<string> HeadersList { get; set; } = new();
    public string? Body { get; set; }
    public bool File { get; set; }
    public bool Gzip { get; set; }
}

public class ResponseBuilder
{
    private readonly HttpResponse _httpResponse = new();

    private static readonly Dictionary<int, string> StatusCodes = new()
    {
        { 200, "200 OK" },
        { 201, "201 Created" },
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

    public ResponseBuilder AsFile()
    {
        _httpResponse.File = true;
        return this;
    }

    public ResponseBuilder WithContentEncodingGzipHeader()
    {
        _httpResponse.Gzip = true;
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
            var contentTypeHeader = $"Content-Type: {(_httpResponse.File ? "application/octet-stream" : "text/plain")}";
            _httpResponse.HeadersList.Add(contentTypeHeader);
            _httpResponse.HeadersList.Add($"Content-Length: {_httpResponse.Body.Length}");
        }
        if (_httpResponse.Gzip)
        {
            _httpResponse.HeadersList.Add("Content-Encoding: gzip");
            _httpResponse.HeadersList.Add("Content-Type: text/plain");
        }
    }
}

public static class Router
{
    public static async Task<string> GetResponse(string requestString)
    {
        var urlPath = GetUrlPath(requestString);
        if (urlPath == "/")
        {
            return new ResponseBuilder()
                .WithResponseCode(200)
                .Build();
        }
        if (urlPath.Contains("/echo/"))
        {
            // handle
            var encodingHeadersResponse = HandleAcceptEncodingHeaders(requestString);
            if (encodingHeadersResponse != null)
            {
                return encodingHeadersResponse;
            }

            var valueToReturn = urlPath.Split('/').Last();
            Console.WriteLine("Value to return: " + valueToReturn);
            return new ResponseBuilder()
                .WithResponseCode(200)
                .WithBody(valueToReturn)
                .Build();
        }

        if (urlPath.TrimEnd('/').Equals("/user-agent", StringComparison.OrdinalIgnoreCase))
        {
            var userAgentHeaderText = GetUserAgentHeader(requestString);
            if (!string.IsNullOrEmpty(userAgentHeaderText))
            {
                return new ResponseBuilder()
                    .WithResponseCode(200)
                    .WithBody(userAgentHeaderText)
                    .Build();
            }
        }

        if (urlPath.StartsWith("/files/"))
        {
            var httpMethod = GetHttpMethod(requestString);
            if (httpMethod == "GET")
            {
                var getFileResult = GetFile(urlPath);
                if (!string.IsNullOrEmpty(getFileResult))
                {
                    return getFileResult;
                }
            }
            if (httpMethod == "POST")
            {
                // ignore the headers for now
                var body = GetBody(requestString);
                if (!string.IsNullOrEmpty(body))
                {
                    await CreateFile(urlPath, body);
                    return new ResponseBuilder()
                        .WithResponseCode(201)
                        .Build();
                }
            }
        }

        return new ResponseBuilder()
            .WithResponseCode(404)
            .Build();
    }

    private static string GetUrlPath(string requestString)
    {
        var requestLines = requestString.Split("\r\n");
        return requestLines[0].Split(' ')[1];
    }

    private static string GetHttpMethod(string requestString)
    {
        var requestLines = requestString.Split("\r\n");
        return requestLines[0].Split(' ')[0];
    }

    private static string? GetUserAgentHeader(string requestString)
    {
        var requestLines = requestString.Split("\r\n");
        var userAgentLine = Array.Find(requestLines, line => line.Contains("User-Agent:"));
        return userAgentLine?.Split(' ')[1];
    }

    private static IEnumerable<string>? GetAcceptEncodingHeader(string requestString)
    {
        var requestLines = requestString.Split("\r\n");
        var acceptEncodingLine = Array.Find(requestLines, line => line.Contains("Accept-Encoding:"));
        return acceptEncodingLine?.Replace("Accept-Encoding:", "").Split(',').Select(x => x.Trim());
    }

    private static string? GetBody(string requestString)
    {
        var requestLines = requestString.Split("\r\n");
        return requestLines.LastOrDefault();
    }

    private static string? GetFileName(string urlPath)
    {
        var regex = new Regex(@"/files/(?<filename>\w*)");
        var match = regex.Match(urlPath);
        return match.Groups["filename"].Value;
    }

    private static string? GetFile(string urlPath)
    {
        var filename = GetFileName(urlPath);
        var tmpDirPath = Environment.GetCommandLineArgs()[2];
        var filePath = tmpDirPath + filename;
        if (File.Exists(filePath))
        {
            Console.WriteLine("File found, attempting to read...");
            var fileText = File.ReadAllText(filePath);
            return new ResponseBuilder()
                .WithResponseCode(200)
                .WithBody(fileText)
                .AsFile()
                .Build();
        }
        return null;
    }

    private static async Task CreateFile(string urlPath, string contents)
    {
        var fileName = GetFileName(urlPath);
        var tmpDirPath = Environment.GetCommandLineArgs()[2];
        var filePath = tmpDirPath + fileName;
        var file = new FileInfo(filePath);
        FileStream fs = file.Exists ? file.Open(FileMode.Truncate) : file.Create();
        byte[] fileData = new UTF8Encoding(true).GetBytes(contents);
        await fs.WriteAsync(fileData, 0, fileData.Length);
        fs.Close();
    }

    private static string? HandleAcceptEncodingHeaders(string requestString)
    {
        var acceptEncodingHeaderValues = GetAcceptEncodingHeader(requestString);

        if (acceptEncodingHeaderValues == null)
        {
            return null;
        }

        var includesGzipEncoding = acceptEncodingHeaderValues.Any(x => x.ToLower() == "gzip");
        if (includesGzipEncoding)
        {
            return new ResponseBuilder()
                .WithResponseCode(200)
                .WithContentEncodingGzipHeader()
                .Build();
        }
        return new ResponseBuilder()
            .WithResponseCode(200)
            .Build();
    }
}