using Microsoft.VisualStudio.TestTools.UnitTesting;
using Yafex;
using System.Linq;
using System.Text;

namespace UnitTests
{
	[TestClass]
	public class KeyBundleTests
	{
		[TestMethod]
		public void TestECB() {
			string content = @"
			DEADBEEFCAFEF00D0001020304050607 // Foobar
			DEADCAFEBEEFF00D0001020304050607 // Foobar2
			";

			KeyBundle bundle = new KeyBundle(content, Encoding.ASCII);
			var keys = bundle.GetKeysEnumerable().ToArray();

			var keyA = new byte[] {
				0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xF0, 0x0D, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07
			};
			var keyB = new byte[] {
				0xDE, 0xAD, 0xCA, 0xFE, 0xBE, 0xEF, 0xF0, 0x0D, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07
			};

			Assert.IsTrue(keyA.SequenceEqual(keys[0].key));
			Assert.IsTrue(keyB.SequenceEqual(keys[1].key));
		}

		[TestMethod]
		public void TestCBC() {
			string content = @"
			DEADBEEFCAFEF00D0001020304050607,000102030405060708090A0B0C0D0E0F // Foobar
			00000000000000000000000000000001,FACEFACEFACEFACEFACEFACEFAFEFACE // Foobar2
			";

			KeyBundle bundle = new KeyBundle(content, Encoding.ASCII);
			var keys = bundle.GetKeysEnumerable().ToArray();

			var keyA = new byte[] {
				0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xF0, 0x0D, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07
			};
			var ivA = new byte[] {
				0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F
			};

			Assert.IsTrue(keyA.SequenceEqual(keys[0].key));
			Assert.IsTrue(ivA.SequenceEqual(keys[0].iv));
		}
	}
}
