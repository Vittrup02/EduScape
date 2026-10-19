using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class PinGeneratorTests
{
    [Fact]
    public void Next_ReturnsSixAsciiDigits()
    {
        var generator = new RandomPinGenerator();

        for (var i = 0; i < 1000; i++)
        {
            var pin = generator.Next();
            Assert.Equal(Session.PinLength, pin.Length);
            Assert.True(pin.All(char.IsAsciiDigit), pin);
        }
    }

    [Fact]
    public void Next_IsSpreadOverTheWholeRange()
    {
        var generator = new RandomPinGenerator();

        var pins = Enumerable.Range(0, 1000).Select(_ => generator.Next()).ToList();

        // 1,000 draws from 1,000,000 values: a handful of repeats at most, and every leading digit occurs (incl. 0).
        Assert.True(pins.Distinct().Count() > 990);
        Assert.Equal(10, pins.Select(p => p[0]).Distinct().Count());
    }
}
