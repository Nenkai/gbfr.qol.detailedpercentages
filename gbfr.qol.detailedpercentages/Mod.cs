using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory;
using Reloaded.Memory.Interfaces;
using Reloaded.Memory.Pointers;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sigscan.Definitions.Structs;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;

using Microsoft.Extensions.Logging.Abstractions;

using NenTools.Reloaded.ScanManager.Interfaces;

using gbfrelink.utility.manager.Interfaces;



using gbfr.qol.detailedpercentages.Configuration;
using gbfr.qol.detailedpercentages.Template;

using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace gbfr.qol.detailedpercentages;

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
    private ControllerEmParam* _controllerEmParamPtr;
    private uint _lastMaxHealth;

    // ui::component::Text
    private TextComponentSetText Wrapper_SetTextComponentText;
    public unsafe delegate void TextComponentSetText(nint a1, GameString* str, uint unkHash, int unk);

    private IHook<ControllerEmParam_UpdateEnemyHealthPercentage> _enemyHealthPercentageHook;
    public delegate void ControllerEmParam_UpdateEnemyHealthPercentage(ControllerEmParam* this_, float v);

    private IHook<PlayerParamUpdate> _playerParamUpdateHook;
    public delegate void PlayerParamUpdate(nint a1);

    private IHook<TextComponent_SetTextFromInt> _setTextComponentFromInt;
    public delegate void TextComponent_SetTextFromInt(nint a1, long number);

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

        WeakReference<IScanManager> scanManagerRef = _modLoader.GetController<IScanManager>();
        if (!scanManagerRef.TryGetTarget(out IScanManager? scanManager))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] ERROR: Unable to get IScanManager?", Color.Red);
            return;
        }

        WeakReference<IUserDefinedParams> userDefinedParamsRef = _modLoader.GetController<IUserDefinedParams>();
        if (!userDefinedParamsRef.TryGetTarget(out IUserDefinedParams? userDefinedParams))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] ERROR: Unable to get IUserDefinedParams?", Color.Red);
            return;
        }

#if DEBUG
        Debugger.Launch();
#endif

        _imageBase = Process.GetCurrentProcess().MainModule!.BaseAddress;
        var memory = Reloaded.Memory.Memory.Instance;

        string? signatureGroup = "";
        if (!userDefinedParams.IsEndlessRagnarok())
        {
            signatureGroup = "granblue_fantasy_relink_v1.3";
        }
        else
        {
            var version = userDefinedParams.GetGameVersion();
            if (version == GameVersion.RelinkEndlessRagnarok_OpenBeta)
                signatureGroup = "granblue_fantasy_relink_open_beta";
            else if (version == GameVersion.RelinkEndlessRagnarok_ClosedBeta)
                signatureGroup = "granblue_fantasy_relink_closed_beta";
            else
                signatureGroup = "granblue_fantasy_relink_er";
        }

        string signaturesFolder = Path.Combine(_modLoader.GetDirectoryForModId(_modConfig.ModId), "Signatures");
        scanManager.InitializeScans(signaturesFolder, _modConfig.ModId);

        // Hook the function that requests text change on health down, remove the rounding
        scanManager.AddScan("EnemyHealthPercentage", signatureGroup, address =>
        {
            // Original function passes the percentage (0f to 1f) as a XMM register

            // original instructions (rounding):
            // vmulss  xmm0, xmm6, cs:dword_7FF604B240C8 (100.0)
            // vroundss xmm0, xmm0, xmm0, 0Ah
            // vcvttss2si edx, xmm0

            int offset = userDefinedParams.IsEndlessRagnarok() ? 0x1E : 0x2D;
            Memory.Instance.SafeWrite((nuint)(address + offset), new byte[] { 0x66, 0x0F, 0x7E, 0xF2, // movd   edx,xmm6 - we're moving to edx as the next instruction is the set value function
                                                               0x90, 0x90, 0x90, 0x90, 0x90,  0x90, 0x90, 0x90,  0x90, 0x90, 0x90, 0x90, 0x90, 0x90 });
            _logger.WriteLine($"[{_modConfig.ModId}] Percentage for enemy health patched (0x{address:X8})", _logger.ColorGreen);

            // call ApplyText
            _enemyHealthPercentageHook = _hooks!.CreateHook<ControllerEmParam_UpdateEnemyHealthPercentage>(ControllerEmParam_UpdateEnemyHealthPercentageImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Successfully hooked UpdateEnemyHealthPercentage (0x{address:X8})", _logger.ColorGreen);
        });


        // Hook the function that requests player param (aka player ui/gauge etc)
        scanManager.AddScan("PlayerParamUpdate", signatureGroup, address =>
        {
            _playerParamUpdateHook = _hooks!.CreateHook<PlayerParamUpdate>(PlayerParamUpdateImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Successfully hooked PlayerParamUpdate (0x{address:X8})", _logger.ColorGreen);
        });


        scanManager.AddScan("SetComponentFromInt", signatureGroup, address =>
        {
            _setTextComponentFromInt = _hooks!.CreateHook<TextComponent_SetTextFromInt>(TextComponent_SetTextFromIntImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Successfully hooked SetTextComponentFromInt (0x{address:X8})", _logger.ColorGreen);
        });

        scanManager.AddScan("TextComponentSetText", signatureGroup, address =>
        {
            Wrapper_SetTextComponentText = _hooks!.CreateWrapper<TextComponentSetText>(address, out nint wrapperAddress);
            _logger.WriteLine($"[{_modConfig.ModId}] Found TextComponentSetText (0x{address:X8})", _logger.ColorGreen);
        });

        scanManager.AddScan("SBAPercentage", signatureGroup, address =>
        {
            // original instructions (rounding):
            // vmulss xmm0, xmm6, cs:dword_7FF604B240C8 (100.0)
            // vcvttss2si edx, xmm0
            Memory.Instance.SafeWrite((nuint)address, new byte[] { 0x66, 0x0F, 0x7E, 0xF2, // movd   edx,xmm6 - we're moving to edx as the next instruction is the set value function
                                                                   0x90, 0x90, 0x90, 0x90,
                                                                   0x90, 0x90, 0x90, 0x90 });

            _logger.WriteLine($"[{_modConfig.ModId}] Percentage for SBA patched (0x{address:X8})", _logger.ColorGreen);
        });

    }

    public void PlayerParamUpdateImpl(nint /* ui::component::ControllerPlParameter01 */ this_)
    {
        // No better way to know where we are coming from so yeah :/
        _isEditingPlayer = true;
        _playerParamUpdateHook.OriginalFunction(this_);
        _isEditingPlayer = false;
    }

    public void ControllerEmParam_UpdateEnemyHealthPercentageImpl(ControllerEmParam* /* ui::component::ControllerEmParam */ this_, float updateDelta)
    {
        _controllerEmParamPtr = this_;

        // No better way to know where we are coming from so yeah :/
        _isEditingEnemyDamage = true;
        _enemyHealthPercentageHook.OriginalFunction(this_, updateDelta);
        _isEditingEnemyDamage = false;
    }

    private nint _enemyDmgStr = Marshal.AllocHGlobal(0x10);
    private nint _sbaStr = Marshal.AllocHGlobal(0x10);

    public void TextComponent_SetTextFromIntImpl(nint /* ui::component::Text */ this_, long number)
    {
        if (_isEditingPlayer)
        {
            float percent = BitConverter.Int32BitsToSingle((int)number);
            string textStr;

            if (number >> 20 > 0) // Hack to check if it's a float lol.
            {
                if (_configuration.ShowDetailledSBA) 
                    textStr = (percent * 100).ToString($"0.{new string('0', _configuration.SBAPrecision)}");
                else
                    textStr = (percent * 100).ToString("0");
            }
            else
                textStr = number.ToString(); // This function is also used for level & health, so don't do anything here
                

            Span<byte> span = new((byte*)_sbaStr, textStr.Length);
            Encoding.ASCII.GetBytes(textStr, span);

            GameString str = new GameString(_sbaStr, (uint)textStr.Length);
            Wrapper_SetTextComponentText(this_, &str, 0x887AE0B0, -1);
        }
        else if (_isEditingEnemyDamage)
        {
            string textStr;

            // This was a test attempt in showing enemy damage rather than %
            if (false)
            {
                Behavior* ent = _controllerEmParamPtr->BehaviorEntity;

                // Refer to https://github.com/Nenkai/gbfr.utility.modtools's dumped reflection data as for
                // how Root is found for instance

                // TODO: Find out how to navigate through objects properly (say: index by name rather than children list)
                // Root aka root01 (id 1) -> base01 (id 2) -> text02 (id 18) -> text02_hp (id 21)
                ui_Object* text02_hp = _controllerEmParamPtr->Root.ObjectPtr->ChildrenBegin[0]->ChildrenBegin[5]->ChildrenBegin[1];

                // TODO: Find the Text component.

                textStr = $"{(ent is not null ? ent->HpStuff->Health : 0)} / {(ent is not null ? ent->HpStuff->HealthMax : _lastMaxHealth)}";

                if (_controllerEmParamPtr->BehaviorEntity != null)
                    _lastMaxHealth = _controllerEmParamPtr->BehaviorEntity->HpStuff->HealthMax;
            }
            else
            {
                float percentage = BitConverter.Int32BitsToSingle((int)number);
                if (_configuration.ShowDetailledEnemyDamage)
                    textStr = (percentage * 100).ToString($"0.{new string('0', _configuration.EnemyDamagePrecision)}");
                else
                    textStr = (percentage * 100).ToString("0");
            }

            Span<byte> span = new Span<byte>((byte*)_enemyDmgStr, textStr.Length);
            Encoding.ASCII.GetBytes(textStr, span);

            GameString str = new GameString(_enemyDmgStr, (uint)textStr.Length);
            Wrapper_SetTextComponentText(this_, &str, 0x887AE0B0, -1);
        }
        else
            _setTextComponentFromInt.OriginalFunction(this_, number);
    }

    public struct GameString
    {
        public nint StringPtr;
        public uint Length;

        public GameString(nint strPtr, uint length)
        {
            StringPtr = strPtr;
            Length = length;
        }
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