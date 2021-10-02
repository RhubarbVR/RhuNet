using Microsoft.VisualStudio.TestTools.UnitTesting;

using RhuNet;

using System;
using System.Collections.Generic;
using System.Text;

namespace RhuNet.Tests
{
    [TestClass()]
    public class RhuClientTests
    {
        [TestMethod()]
        public void RhuClientTest()
        {
            var client = new RhuClient("52.1.1.1", 50, "uuid");
        }
    }
}