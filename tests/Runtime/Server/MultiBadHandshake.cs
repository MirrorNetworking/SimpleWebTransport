using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirror.SimpleWeb.Tests.Server
{
    [Category("SimpleWebTransport")]
    public class MultiBadHandshake : SimpleWebTestBase
    {
        protected override bool StartServer => true;

        List<TcpClient> badClients = new List<TcpClient>();
        List<Task<RunNode.Result>> goodClients = new List<Task<RunNode.Result>>();

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            foreach (TcpClient bad in badClients)
            {
                bad.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator MultipleGoodAndBadClients()
        {
            int connectIndex = 1;
            transport.OnServerConnected.AddListener((connId) =>
            {
                Assert.That(connId == connectIndex, "Clients should be connected in order with the next index");
                connectIndex++;
            });
            const int goodClientCount = 10;
            for (int i = 0; i < goodClientCount * 2; i++)
            {
                // alternate between good and bad clients
                if (i % 2 == 0)
                {
                    Task<TcpClient> createTask = CreateBadClient();
                    while (!createTask.IsCompleted) { yield return null; }
                    TcpClient client = createTask.Result;
                    Assert.That(client.Connected, Is.True, "Client should have connected");
                    badClients.Add(client);
                }
                else
                {
                    // connect good client
                    Task<RunNode.Result> task = RunNode.RunAsync("ConnectAndClose.js");
                    goodClients.Add(task);
                    yield return null;
                }
            }

            // wait for timeout so bad clients disconnect
            yield return new WaitForSeconds(timeout / 1000);
            // wait extra second for stuff to process
            yield return new WaitForSeconds(1);

            Assert.That(onConnect, Has.Count.EqualTo(goodClientCount), "Connect should not be called");
            Assert.That(onDisconnect, Has.Count.EqualTo(goodClientCount), "Disconnect should not be called");
            Assert.That(onData, Has.Count.EqualTo(0), "Data should not be called");

            for (int i = 0; i < 10; i++)
            {
                Task<RunNode.Result> task = goodClients[0];
                Assert.That(task.IsCompleted, Is.True, "Take should have been completed");

                RunNode.Result result = task.Result;

                result.AssetTimeout(false);
                result.AssetOutput(
                    "Connection opened",
                    $"Closed after 2000ms"
                );
                result.AssetErrors();
            }
        }
    }
}
