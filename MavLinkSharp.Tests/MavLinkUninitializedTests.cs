namespace MavLinkSharp.Tests
{
    public class MavLinkUninitializedTests
    {
        [Fact]
        public void TryParse_BeforeInitialize_ThrowsException()
        {
            // Arrange
            var packet = new byte[] { 0xFD, 0x09, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x03, 0x51, 0x04 };

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => new Frame().TryParse(packet));
        }
    }
}
