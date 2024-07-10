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
var requestLines = requestString.Split("\r\n");
var urlPath = requestLines[0].Split(' ')[1];

var responseString200 = "HTTP/1.1 200 OK\r\n\r\n";
var responseString404 = "HTTP/1.1 404 Not Found\r\n\r\n";

var responseString = urlPath == "/" ? responseString200 : responseString404;

var sendBytes = Encoding.ASCII.GetBytes(responseString);
clientSocket.Send(sendBytes);
Console.WriteLine("Message Sent /> : " + responseString);
