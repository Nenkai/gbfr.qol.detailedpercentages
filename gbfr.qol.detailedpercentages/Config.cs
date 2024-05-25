using gbfr.qol.detailedpercentages.Template.Configuration;

using Reloaded.Mod.Interfaces.Structs;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace gbfr.qol.detailedpercentages.Configuration
{
    public class Config : Configurable<Config>
    {
        /*
            User Properties:
                - Please put all of your configurable properties here.
    
            By default, configuration saves as "Config.json" in mod user config folder.    
            Need more config files/classes? See Configuration.cs
    
            Available Attributes:
            - Category
            - DisplayName
            - Description
            - DefaultValue

            // Technically Supported but not Useful
            - Browsable
            - Localizable

            The `DefaultValue` attribute is used as part of the `Reset` button in Reloaded-Launcher.
        */

        [DisplayName("Show Detailed Enemy Damage")]
        [DefaultValue(true)]
        public bool ShowDetailledEnemyDamage { get; set; } = true;

        [DisplayName("Enemy Damage Precision")]
        [Description("Number of digits after period.")]
        [SliderControlParams(minimum: 0.0, maximum: 4.0, smallChange: 1.0, tickFrequency: 1, isSnapToTickEnabled: true, tickPlacement: SliderControlTickPlacement.BottomRight,
            showTextField: true,
            isTextFieldEditable: true,
            textValidationRegex: "\\d{1-3}")]
        [DefaultValue(2)]
        public int EnemyDamagePrecision { get; set; } = 2;

        [DisplayName("Show Detailed SBA")]
        [DefaultValue(true)]
        public bool ShowDetailledSBA { get; set; } = true;

        [DisplayName("SBA Precision")]
        [Description("Number of digits after period.")]
        [SliderControlParams(minimum: 0.0, maximum: 4.0, smallChange: 1.0, tickFrequency: 1, isSnapToTickEnabled: true, tickPlacement: SliderControlTickPlacement.BottomRight,
            showTextField: true,
            isTextFieldEditable: true,
            textValidationRegex: "\\d{1-3}")]
        [DefaultValue(1)]
        public int SBAPrecision { get; set; } = 1;
    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
        // 
    }
}
