using System.Diagnostics;

using Reloaded.Mod.Interfaces;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sigscan.Definitions.Structs;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

using gbfr.qol.detailledpercentages.Configuration;
using gbfr.qol.detailledpercentages.Template;
using Reloaded.Memory;
using Reloaded.Memory.Interfaces;
using Reloaded.Hooks.ReloadedII.Interfaces;
using System.Runtime.InteropServices;
using System.Text;


namespace gbfr.qol.detailledpercentages;

/// <summary>
/// Your mod logic goes here.
/// </summary>
public unsafe class Mod : ModBase // <= Do not Remove.
{
    /// <summary>
    /// Provides access to the mod loader API.
    /// </summary>
    private readonly IModLoader _modLoader;

    /// <summary>
    /// Provides access to the Reloaded.Hooks API.
    /// </summary>
    /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
    private readonly IReloadedHooks? _hooks;

    /// <summary>
    /// Provides access to the Reloaded logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Entry point into the mod, instance that created this class.
    /// </summary>
    private readonly IMod _owner;

    /// <summary>
    /// Provides access to this mod's configuration.
    /// </summary>
    private Config _configuration;

    /// <summary>
    /// The configuration of the currently executing mod.
    /// </summary>
    private readonly IModConfig _modConfig;

    private static IStartupScanner? _startupScanner = null!;

    private nint _imageBase;

    public bool _isEditingEnemyDamage = false;
    public bool _isEditingPlayer = false;

    // ui::component::Text
    private TextComponentSetTextDelegate Wrapper_SetTextComponentText;
    public unsafe delegate void TextComponentSetTextDelegate(void* a1, GameString* str, uint unkHash, int unk);

    private IHook<UpdateEnemyHealthPercentageDelegate> _enemyHealthPercentageHook;
    public delegate void UpdateEnemyHealthPercentageDelegate(void* a1);

    private IHook<PlayerParamUpdateDelegate> _playerParamUpdateHook;
    public delegate void PlayerParamUpdateDelegate(void* a1);

    private IHook<SetTextComponentFromIntDelegate> _setTextComponentFromInt;
    public delegate void SetTextComponentFromIntDelegate(void* a1, long number);

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _owner = context.Owner;
        _configuration = context.Configuration;
        _modConfig = context.ModConfig;

        var startupScannerController = _modLoader.GetController<IStartupScanner>();
        if (startupScannerController == null || !startupScannerController.TryGetTarget(out _startupScanner))
        {
            return;
        }

        Debugger.Launch();

        _imageBase = Process.GetCurrentProcess().MainModule!.BaseAddress;
        var memory = Reloaded.Memory.Memory.Instance;

        // Hook the function that requests text change on health down, remove the rounding
        SigScan("55 56 57 53 48 83 EC ?? 48 8D 6C 24 ?? C5 F8 29 75 ?? 48 C7 45 ?? ?? ?? ?? ?? C5 F8 28 F1 48 89 CE 48 8B 89", "", address =>
        {
            // Original function passes the percentage (0f to 1f) as a XMM register

            // original instructions:
            // vmulss  xmm0, xmm6, cs:dword_7FF604B240C8 (100.0)
            // vroundss xmm0, xmm0, xmm0, 0Ah
            // vcvttss2si edx, xmm0
            Memory.Instance.SafeWrite((nuint)(address + 0x2D), new byte[] { 0x66, 0x0F, 0x7E, 0xF2, // movd   edx,xmm6
                                                                           0x90, 0x90, 0x90, 0x90, 0x90,  0x90, 0x90, 0x90,  0x90, 0x90, 0x90, 0x90, 0x90, 0x90 });
            _logger.WriteLine($"[gbfr.qol.detailledpercentages] Percentage for enemy health patched (0x{address:X8})", _logger.ColorGreen);

            // call ApplyText
            _enemyHealthPercentageHook = _hooks!.CreateHook<UpdateEnemyHealthPercentageDelegate>(OnWriteDamage, address).Activate();
            _logger.WriteLine($"[gbfr.qol.detailledpercentages] Successfully hooked UpdateEnemyHealthPercentage (0x{address:X8})", _logger.ColorGreen);
        });

        // Hook the function that requests player param (aka player ui/gauge etc)
        SigScan("55 41 57 41 56 41 55 41 54 56 57 53 48 81 EC ?? ?? ?? ?? 48 8D AC 24 ?? ?? ?? ?? C5 F8 29 BD ?? ?? ?? ?? C5 F8 29 75 ?? 48 C7 45 ?? ?? ?? ?? ?? 49 89 CD", "", address =>
        {
            _playerParamUpdateHook = _hooks!.CreateHook<PlayerParamUpdateDelegate>(OnPlayerUIUpdate, address).Activate();
            _logger.WriteLine($"[gbfr.qol.detailledpercentages] Successfully hooked PlayerParamUpdate (0x{address:X8})", _logger.ColorGreen);
        });

        SigScan("41 56 56 57 53 48 83 EC ?? 49 89 CE 89 D0", "", address =>
        {
            _setTextComponentFromInt = _hooks!.CreateHook<SetTextComponentFromIntDelegate>(TextComponent_SetTextFromInt, address).Activate();
            _logger.WriteLine($"[gbfr.qol.detailledpercentages] Successfully hooked SetTextComponentFromInt (0x{address:X8})", _logger.ColorGreen);
        });

        SigScan("55 41 57 41 56 41 55 41 54 56 57 53 48 83 EC ?? 48 8D 6C 24 ?? 48 C7 45 ?? ?? ?? ?? ?? 45 89 C7 49 89 D2", "", address =>
        {
            Wrapper_SetTextComponentText = _hooks!.CreateWrapper<TextComponentSetTextDelegate>(address, out nint wrapperAddress);
            _logger.WriteLine($"[gbfr.qol.detailledpercentages] Found TextComponentSetText (0x{address:X8})", _logger.ColorGreen);
        });

        SigScan("C5 CA 59 05 ?? ?? ?? ?? C5 FA 2C D0 E8 ?? ?? ?? ?? C4 C1 7A 11 B5 ?? ?? ?? ?? 49 8B 46", "", address =>
        {
            // original instructions:
            // vmulss xmm0, xmm6, cs:dword_7FF604B240C8 (100.0)
            // vcvttss2si edx, xmm0
            Memory.Instance.SafeWrite((nuint)address, new byte[] { 0x66, 0x0F, 0x7E, 0xF2, // movd   edx,xmm6
                                                                   0x90, 0x90, 0x90, 0x90,
                                                                   0x90, 0x90, 0x90, 0x90 });

            _logger.WriteLine($"[gbfr.qol.detailledpercentages] Percentage for SBA patched (0x{address:X8})", _logger.ColorGreen);
        });
    }

    private void SigScan(string pattern, string name, Action<nint> action)
    {
        _startupScanner?.AddMainModuleScan(pattern, result =>
        {
            if (!result.Found)
            {
                return;
            }
            action(_imageBase + result.Offset);
        });
    }

    public void OnPlayerUIUpdate(void* /* ui::component::ControllerPlParameter01 */ this_)
    {
        // No better way to know where we are coming from so yeah :/
        _isEditingPlayer = true;
        _playerParamUpdateHook.OriginalFunction(this_);
        _isEditingPlayer = false;
    }

    public void OnWriteDamage(void* /* ui::component::ControllerEmParam */ this_)
    {
        // No better way to know where we are coming from so yeah :/
        _isEditingEnemyDamage = true;
        _enemyHealthPercentageHook.OriginalFunction(this_);
        _isEditingEnemyDamage = false;
    }

    private nint _enemyDmgStr = Marshal.AllocHGlobal(0x10);
    private nint _sbaStr = Marshal.AllocHGlobal(0x10);

    public void TextComponent_SetTextFromInt(void* /* ui::component::Text */ this_, long number)
    {
        if (_isEditingPlayer && _configuration.ShowDetailledSBA && number >> 20 > 0) // Hack to check if it's a float lol.
        {
            float sbaPercent = BitConverter.Int32BitsToSingle((int)number);
            string sbaPercentStr = $"{sbaPercent * 100:0.0}";

            Span<byte> span = new Span<byte>((byte*)_sbaStr, sbaPercentStr.Length);
            Encoding.ASCII.GetBytes(sbaPercentStr, span);

            GameString str = new GameString()
            {
                StringPtr = _sbaStr,
                Length = (uint)sbaPercentStr.Length
            };

            Wrapper_SetTextComponentText(this_, &str, 0x887AE0B0, -1);
        }
        else if (_isEditingEnemyDamage && _configuration.ShowDetailledEnemyDamage)
        {
            float percentage = BitConverter.Int32BitsToSingle((int)number);
            string percentageStr = $"{percentage * 100:0.00}";

            Span<byte> span = new Span<byte>((byte*)_enemyDmgStr, percentageStr.Length);
            Encoding.ASCII.GetBytes(percentageStr, span);

            GameString str = new GameString()
            {
                StringPtr = _enemyDmgStr,
                Length = (uint)percentageStr.Length
            };

            Wrapper_SetTextComponentText(this_, &str, 0x887AE0B0, -1);
        }
        else
            _setTextComponentFromInt.OriginalFunction(this_, number);
    }

    public struct GameString
    {
        public nint StringPtr;
        public uint Length;
    }

    #region Standard Overrides
    public override void ConfigurationUpdated(Config configuration)
    {
        // Apply settings from configuration.
        // ... your code here.
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
    }
    #endregion

    #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}