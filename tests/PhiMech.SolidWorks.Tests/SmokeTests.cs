using System;
using System.IO;
using PhiMech.SolidWorks.Commands;
using PhiMech.SolidWorks.Print;
using Xunit;

namespace PhiMech.SolidWorks.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void ExampleEnvelopeIsNeutralAndExplicit()
        {
            var settings = new PrintSettings();
            Assert.Equal(new[] { 200.0, 200.0, 200.0 }, settings.EnvelopeMm);
            Assert.Equal("", settings.ProfileDirectory);
        }

        [Fact]
        public void FitCheckUsesSuppliedDimensions()
        {
            var fit = FitCheck.Evaluate(new[] { 180.0, 190.0, 195.0 }, new[] { 200.0, 200.0, 200.0 });
            Assert.True(fit.FitsAsExported);
        }

        [Fact]
        public void IrStubContainsOneGenericImportAndNoDrawing()
        {
            var document = ExportIrStubCommand.Build("sample-part", "Sample Part", "sample.step", "A");
            Assert.Equal("generic", document.Company);
            Assert.Equal("mm", document.Units);
            var component = Assert.Single(document.Components);
            Assert.Equal("import", component.Source.Kind);
            Assert.Equal("sample.step", component.Source.Path);
            Assert.False(document.ToJson().Contains("drawing", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void SettingsRoundTripKeepsUserProfile()
        {
            var path = Path.Combine(Path.GetTempPath(), "phimech-settings-" + Guid.NewGuid() + ".json");
            try
            {
                new PrintSettings { EnvelopeMm = new[] { 220.0, 210.0, 200.0 }, ProfileDirectory = "my-profiles" }.Save(path);
                var loaded = PrintSettings.Load(path);
                Assert.Equal(new[] { 220.0, 210.0, 200.0 }, loaded.EnvelopeMm);
                Assert.Equal("my-profiles", loaded.ProfileDirectory);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
