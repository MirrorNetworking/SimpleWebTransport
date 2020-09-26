#define SIMPLE_WEB_INFO_LOG
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Mirror.SimpleWeb
{
    internal class Handshake
    {
        private const int ResponseLength = 129;
        private const int KeyLength = 24;
        const string KeyHeaderString = "Sec-WebSocket-Key: ";
        const string HandshakeGUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        readonly object lockObj = new object();
        readonly byte[] readBuffer = new byte[3000];
        readonly byte[] keyBuffer = new byte[60];
        readonly byte[] response = new byte[ResponseLength];
        readonly byte[] endOfHeader;
        readonly SHA1 sha1 = SHA1.Create();

        public Handshake()
        {
            Encoding.UTF8.GetBytes(HandshakeGUID, 0, HandshakeGUID.Length, keyBuffer, KeyLength);
            endOfHeader = new byte[4] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
        }
        ~Handshake()
        {
            sha1.Dispose();
        }

        /// <summary>
        /// Clears buffers so that data can't be used by next request
        /// </summary>
        void ClearBuffers()
        {
            Array.Clear(readBuffer, 0, 300);
            Array.Clear(readBuffer, 0, 24);
            Array.Clear(response, 0, ResponseLength);
        }

        public bool TryHandshake(Connection conn)
        {
            TcpClient client = conn.client;
            Stream stream = conn.stream;

            //Console.WriteLine("****Handshake****");
            //while (true)
            //{
            //    byte[] getHeader = new byte[3];
            //    bool success = ReadHelper.SafeRead(stream, getHeader, 0, 1);
            //    if (!success)
            //        return false;

            //    Console.Write((char)getHeader[0]);
            //}
            try
            {
                byte[] getHeader = new byte[3];
                bool success = ReadHelper.SafeRead(stream, getHeader, 0, 3);
                if (!success)
                    return false;

                if (!IsGet(getHeader))
                {
                    Log.Info($"First bytes from client was not 'GET' for handshake, instead was {string.Join("-", getHeader.Select(x => x.ToString()))}");
                    return false;
                }
            }
            catch (Exception e) { Debug.LogException(e); return false; }

            // lock so that buffers can only be used by this thread
            lock (lockObj)
            {
                try
                {
                    //return BatchReadsForHandshake(stream);
                    bool success = ReadToEndForHandshake(stream);
                    if (success)
                        Log.Info($"Sent Handshake {conn}");
                    return success;
                    //return ReadAvailableForHandsake(client, stream);
                }
                catch (Exception e) { Debug.LogException(e); return false; }
                finally
                {
                    ClearBuffers();
                }
            }
        }


        private bool ReadAvailableForHandsake(TcpClient client, Stream stream)
        {
            int length = client.Available;
            bool success = ReadHelper.SafeRead(stream, readBuffer, 0, length);
            if (!success)
                return false;

            string msg = Encoding.UTF8.GetString(readBuffer, 0, length);

            AcceptHandshake(stream, msg);
            return true;
        }

        private bool ReadToEndForHandshake(Stream stream)
        {
            int? readCountOrFail = ReadHelper.SafeReadTillMatch(stream, readBuffer, 0, endOfHeader);
            if (!readCountOrFail.HasValue)
                return false;

            int readCount = readCountOrFail.Value;

            string msg = Encoding.UTF8.GetString(readBuffer, 0, readCount);

            AcceptHandshake(stream, msg);
            return true;
        }

        private bool BatchReadsForHandshake(Stream stream)
        {
            int bufferIndex = 0;
            bool success;
            string part;
            while (true)
            {

                Debug.Log(stream.Length);
                bufferIndex = readString(stream, bufferIndex, KeyHeaderString.Length, out success, out part);
                if (!success)
                    return false;

                int keyIndex = part.IndexOf(KeyHeaderString);
                if (keyIndex != -1)
                {
                    Debug.Log("found");

                    int keyStart = keyIndex + KeyHeaderString.Length;
                    if (keyStart + KeyLength > bufferIndex) // havn't read all of key
                    {
                        int needToRead = keyIndex + KeyHeaderString.Length + KeyLength - bufferIndex;
                        bufferIndex = readString(stream, bufferIndex, needToRead, out success, out part);
                        if (!success)
                            return false;
                    }

                    Encoding.UTF8.GetBytes(part, keyStart, KeyLength, keyBuffer, 0);


                    CreateResponse();

                    stream.Write(response, 0, ResponseLength);
                    Log.Info("Sent Handshake");
                }
            }
        }

        private int readString(Stream stream, int offset, int length, out bool success, out string part)
        {
            success = ReadHelper.SafeRead(stream, readBuffer, offset, length);
            offset += KeyHeaderString.Length;

            part = Encoding.UTF8.GetString(readBuffer, 0, offset);
            return offset;
        }

        bool IsGet(byte[] getHeader)
        {
            // just check bytes here instead of using Encoding.UTF8
            return getHeader[0] == 71 && // G
                   getHeader[1] == 69 && // E
                   getHeader[2] == 84;   // T
        }

        void AcceptHandshake(Stream stream, string msg)
        {
            GetKey(msg, keyBuffer);
            CreateResponse();

            stream.Write(response, 0, ResponseLength);
        }

        void CreateResponse()
        {
            byte[] keyHash = sha1.ComputeHash(keyBuffer);

            string keyHashString = Convert.ToBase64String(keyHash);
            // compiler should merge these strings into 1 string before format
            string message = string.Format(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: {0}\r\n\r\n",
                keyHashString);

            Encoding.UTF8.GetBytes(message, 0, ResponseLength, response, 0);
        }

        static void GetKey(string msg, byte[] keyBuffer)
        {
            int start = msg.IndexOf(KeyHeaderString) + KeyHeaderString.Length;

            Encoding.UTF8.GetBytes(msg, start, KeyLength, keyBuffer, 0);
        }
    }
}