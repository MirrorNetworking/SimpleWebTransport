using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirror.SimpleWeb.Tests.Server
{
    [Category("SimpleWebTransport")]
    public class StartAndStopTest : SimpleWebTestBase
    {
        protected override bool StartServer => false;

        [UnityTest]
        public IEnumerator ServerCanStartAndStopWithoutErrors()
        {
            SimpleWebTransport transport = CreateTransport<SimpleWebTransport>();

            server.ServerStart();
            Assert.That(server.ServerActive(), Is.True);
            yield return new WaitForSeconds(0.2f);
            Assert.That(server.ServerActive(), Is.True);

            server.ServerStop();
            Assert.That(server.ServerActive(), Is.False);
            yield return new WaitForSeconds(0.2f);
            Assert.That(server.ServerActive(), Is.False);
        }


        [UnityTest]
        public IEnumerator CanStart2ndServerAfterFirstSTops()
        {
            // use {} block for local variable scope
            {
                SimpleWebTransport transport = CreateTransport<SimpleWebTransport>();

                server.ServerStart();
                Assert.That(server.ServerActive(), Is.True);
                yield return new WaitForSeconds(0.2f);
                Assert.That(server.ServerActive(), Is.True);

                server.ServerStop();
                Assert.That(server.ServerActive(), Is.False);
            }

            {
                SimpleWebTransport transport = CreateTransport<SimpleWebTransport>();

                server.ServerStart();
                Assert.That(server.ServerActive(), Is.True);
                yield return new WaitForSeconds(0.2f);
                Assert.That(server.ServerActive(), Is.True);

                server.ServerStop();
                Assert.That(server.ServerActive(), Is.False);
            }
        }
    }
}
