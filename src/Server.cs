using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace codecrafters_http_server;

public class Program
{
    public static async Task Main(string[] args)
    {
        var router2 = new Router2();
        router2.AddRoute("first/second/third", "GET", () => Console.WriteLine("GET first/second/third"));
        var action = router2.MatchRoute("first/second/third", "GET");
        Console.WriteLine("**********************************");
        Console.WriteLine(action == null);
        action?.Invoke();

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
        using (clientSocket)
        {
            try
            {
                var requestBuffer = new byte[1024];
                var requestLength = await networkStream.ReadAsync(requestBuffer, 0, requestBuffer.Length);
                var requestString = Encoding.ASCII.GetString(requestBuffer, 0, requestLength);

                var request = HttpRequestFactory.Create(requestString);
                var responseBytes = await Router.GetResponse(request);

                clientSocket.Send(responseBytes);
                Console.WriteLine("Message Sent /> : " + Encoding.ASCII.GetString(responseBytes));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }
    }
}

public class HttpResponse
{
    public const string HttpVersion = "HTTP/1.1";
    public string ResponseCodeAndDescription { get; set; } = string.Empty;
    public List<string> HeadersList { get; set; } = new();
    public byte[]? Body { get; set; }
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
            throw new ArgumentException("Unsupported HTTP status code, use 200, 201 or 404");
        }
        _httpResponse.ResponseCodeAndDescription = description;
        return this;
    }

    public ResponseBuilder WithBody(string body)
    {
        return WithBody(Encoding.ASCII.GetBytes(body));
    }

    public ResponseBuilder WithBody(byte[] body)
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

    public byte[] Build()
    {
        AddContentHeaders();
        return BuildResponseBytes();
    }

    private byte[] BuildResponseBytes()
    {
        var responsePartsExceptBody = new List<string?>
        {
            $"{HttpResponse.HttpVersion} {_httpResponse.ResponseCodeAndDescription}",
            String.Join("\r\n", _httpResponse.HeadersList),
            ""
        };
        var responseStringExceptBody = String.Join("\r\n", responsePartsExceptBody);
        var responseBytesExceptBody = Encoding.ASCII.GetBytes(responseStringExceptBody + "\r\n");

        if (_httpResponse.Body != null)
        {
            var responseBytes = new byte[responseBytesExceptBody.Length + _httpResponse.Body.Length];
            Buffer.BlockCopy(responseBytesExceptBody, 0, responseBytes, 0, responseBytesExceptBody.Length);
            Buffer.BlockCopy(_httpResponse.Body, 0, responseBytes, responseBytesExceptBody.Length, _httpResponse.Body.Length);
            return responseBytes;
        }

        return responseBytesExceptBody;
    }

    private void AddContentHeaders()
    {
        if (_httpResponse.Body != null)
        {
            var contentTypeHeader = $"Content-Type: {(_httpResponse.File ? "application/octet-stream" : "text/plain")}";
            _httpResponse.HeadersList.Add(contentTypeHeader);
            _httpResponse.HeadersList.Add($"Content-Length: {_httpResponse.Body.Length}");
        }
        if (_httpResponse.Gzip)
        {
            _httpResponse.HeadersList.Add("Content-Encoding: gzip");
        }
    }
}

public static class Router
{
    public static async Task<byte[]> GetResponse(HttpRequest httpRequest)
    {
        var urlPath = httpRequest.UrlPath;
        if (urlPath == "/")
        {
            return new ResponseBuilder()
                .WithResponseCode(200)
                .Build();
        }
        if (urlPath.Contains("/echo/"))
        {
            var encodingHeadersResponse = HandleAcceptEncodingHeaders(httpRequest);
            if (encodingHeadersResponse != null)
            {
                return encodingHeadersResponse;
            }

            var valueToReturn = urlPath.Split('/').Last();
            return new ResponseBuilder()
                .WithResponseCode(200)
                .WithBody(valueToReturn)
                .Build();
        }

        if (urlPath.TrimEnd('/').Equals("/user-agent", StringComparison.OrdinalIgnoreCase))
        {
            var userAgentHeaderText = httpRequest.GetHeader("User-Agent");

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
            var httpMethod = httpRequest.Method;
            if (httpMethod == "GET")
            {
                var getFileResult = GetFile(urlPath);
                if (getFileResult != null)
                {
                    return getFileResult;
                }
            }
            if (httpMethod == "POST")
            {
                // ignore the headers for now
                var body = httpRequest.Body;
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

    private static byte[]? GetFile(string urlPath)
    {
        var filename = RequestService.GetFileName(urlPath);
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
        var fileName = RequestService.GetFileName(urlPath);
        var tmpDirPath = Environment.GetCommandLineArgs()[2];
        var filePath = tmpDirPath + fileName;
        var file = new FileInfo(filePath);
        FileStream fs = file.Exists ? file.Open(FileMode.Truncate) : file.Create();
        byte[] fileData = new UTF8Encoding(true).GetBytes(contents);
        await fs.WriteAsync(fileData, 0, fileData.Length);
        fs.Close();
    }

    private static byte[]? HandleAcceptEncodingHeaders(HttpRequest httpRequest)
    {
        var acceptEncodingHeaderValues = httpRequest.GetCommaSeperatedHeader("Accept-Encoding");

        if (acceptEncodingHeaderValues == null)
        {
            return null;
        }

        var includesGzipEncoding = acceptEncodingHeaderValues.Any(x => x.ToLower() == "gzip");
        if (includesGzipEncoding)
        {
            var urlPath = httpRequest.UrlPath;
            var valueToCompress = urlPath.Split('/').Last();
            if (valueToCompress == null)
            {
                throw new InvalidDataException();
            }
            var compressedBody = Compress(valueToCompress);
            return new ResponseBuilder()
                .WithResponseCode(200)
                .WithBody(compressedBody)
                .WithContentEncodingGzipHeader()
                .Build();
        }
        return new ResponseBuilder()
            .WithResponseCode(200)
            .Build();
    }
    
    private static byte[] Compress(string text)
    {
        var textBytes = Encoding.ASCII.GetBytes(text);
        using var ms = new MemoryStream();
        using (var compressionStream = new GZipStream(ms, CompressionMode.Compress))
        {
            compressionStream.Write(textBytes, 0, textBytes.Length);
        }
        return ms.ToArray();
    }
}

public static class RequestService
{
    public static string? GetFileName(string urlPath)
    {
        var regex = new Regex(@"/files/(?<filename>\w*)");
        var match = regex.Match(urlPath);
        return match.Groups["filename"].Value;
    }
}

public record HttpRequest(string Method, string UrlPath, Dictionary<string, string> Headers, string? Body)
{
    public string? GetHeader(string headerName)
    {
        // "Accept-Encoding" or "User-Agent"
        try
        {
            return Headers[headerName];
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Exception trying to access header {headerName} - {ex.Message} - {ToString()}");
            return null;
        }
    }

    public IEnumerable<string>? GetCommaSeperatedHeader(string headerName)
    {
        return GetHeader("Accept-Encoding")?.Split(',').Select(x => x.Trim());
    }

    public override string ToString()
    {
        string headers = "";
        foreach (var header in Headers)
        {
            headers += $"{header.Key}: {header.Value}\r\n";
        }
        return $@"
            Method {Method}
            UrlPath {UrlPath}
            Headers:
            {headers}
        ";
    }
}

public static class HttpRequestFactory
{
    public static HttpRequest Create(string requestString)
    {
        var requestLines = requestString.Split("\r\n");
        var method = requestLines[0].Split(' ')[0];
        var urlPath = requestLines[0].Split(' ')[1];
        var body = requestLines.LastOrDefault();

        var headerLines = requestString.Split("\r\n\r\n")[0].Split("\r\n")[1..];
        var headers = new Dictionary<string, string>();
        foreach (var headerLine in headerLines)
        {
            var kv = headerLine.Split(": ");
            headers.Add(kv[0], kv[1]);
        }

        return new HttpRequest(method, urlPath, headers, body);
    }
}

// alevel-chemistry-tutor/api/questions/reactingMasses
//{
//  segment: 'alevel-chemistry-tutor',
//  children: {
//    segment: 'api',
//    children: {
//      segment: 'questions',
//      children: {
//        segment: 'reactingMasses',
//        methods: {
//          GET: () => {},
//        },
//      },
//    },
//  },
//}

//public enum HttpMethods
//{
//    GET,
//    POST,
//}

public class RouteNode(string segment)
{
    public string Segment { get; set; } = segment;
    public Dictionary<string, RouteNode> Children = new ();
    public Dictionary<string, Action>? Handlers;

    public void AddRoute(string[] segments, int index, string httpMethod, Action handler)
    {
        if (index == segments.Length)
        {
            Handlers ??= new Dictionary<string, Action>();
            Handlers.Add(httpMethod, handler);
            return;
        }

        var segment = segments[index];
        if (!Children.ContainsKey(segment))
        {
            Children.Add(segment, new RouteNode(segment));
        }

        Children[segment].AddRoute(segments, index + 1, httpMethod, handler);
    }

    public RouteNode? Match(string[] segments, int index)
    {
        Console.WriteLine($"In match, index: {index}");
        if (index == segments.Length)
        {
            return this;
        }

        var segment = segments[index];
        if (Children.ContainsKey(segment))
        {
            return Children[segment].Match(segments, index + 1);
        }
        return null;
    }
}

public class Router2
{
    private RouteNode _root { get; } = new ("");

    public void AddRoute(string path, string httpMethod, Action handler)
    {
        var pathSegments = path.Trim('/').Split('/');
        _root.AddRoute(pathSegments, 0, httpMethod, handler);
    }

    public Action? MatchRoute(string path, string httpMethod)
    {
        var pathSegments = path.Trim('/').Split('/');
        var node = _root.Match(pathSegments, 0);
        return node?.Handlers?[httpMethod];
    }
}