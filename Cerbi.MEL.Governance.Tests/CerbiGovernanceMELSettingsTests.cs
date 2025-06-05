using Cerbi;
using Xunit;

namespace Cerbi.Tests
{
    public class CerbiGovernanceMELSettingsTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var settings = new CerbiGovernanceMELSettings();
            Assert.Equal("default", settings.Profile);
            Assert.Equal("cerbi_governance.json", settings.ConfigPath);
            Assert.True(settings.Enabled);
        }

        [Fact]
        public void Can_SetProperties()
        {
            var settings = new CerbiGovernanceMELSettings
            {
                Profile = "Orders",
                ConfigPath = "/etc/config.json",
                Enabled = false
            };

            Assert.Equal("Orders", settings.Profile);
            Assert.Equal("/etc/config.json", settings.ConfigPath);
            Assert.False(settings.Enabled);
        }
    }
}