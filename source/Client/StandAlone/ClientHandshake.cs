#define SIMPLE_WEB_INFO_LOG

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Mirror.SimpleWeb
{
    /// <summary>
    /// Handles Handshake to the server when it first connects
    /// <para>The client handshake does not need buffers to reduce allocations since it only happens once</para>
    /// </summary>
    internal class ClientHandshake
    {
        public bool TryHandshake(Connection conn, Uri uri)
        {
            try
            {
                Stream stream = conn.stream;

                byte[] keyBuffer = new byte[16];
                new System.Random().NextBytes(keyBuffer);

                string key = Convert.ToBase64String(keyBuffer);
                string keySum = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                byte[] keySumBytes = Encoding.UTF8.GetBytes(keySum);
                byte[] keySumHash = SHA1.Create().ComputeHash(keySumBytes);

                string expectedResponse = Convert.ToBase64String(keySumHash);
                string handshake =
                    $"GET /chat HTTP/1.1\r\n" +
                    $"Host: {uri.Host}:{uri.Port}\r\n" +
                    $"Upgrade: websocket\r\n" +
                    $"Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Key: {key}\r\n" +
                    $"Sec-WebSocket-Version: 13\r\n" +
                    "\r\n";
                byte[] encoded = Encoding.UTF8.GetBytes(handshake);
                stream.Write(encoded, 0, encoded.Length);

                byte[] responseBuffer = new byte[1000];

                int? lengthOrNull = ReadHelper.SafeReadTillMatch(stream, responseBuffer, 0, new byte[4] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' });

                if (!lengthOrNull.HasValue)
                {
                    Debug.LogError("Connected closed before handshake");
                    return false;
                }

                string responseString = Encoding.UTF8.GetString(responseBuffer, 0, lengthOrNull.Value);

                string acceptHeader = "Sec-WebSocket-Accept: ";
                int startIndex = responseString.IndexOf(acceptHeader) + acceptHeader.Length;
                int endIndex = responseString.IndexOf("\r\n", startIndex);
                string responseKey = responseString.Substring(startIndex, endIndex - startIndex);

                if (responseKey != expectedResponse)
                {
                    Debug.LogError("Reponse key incorrect");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}