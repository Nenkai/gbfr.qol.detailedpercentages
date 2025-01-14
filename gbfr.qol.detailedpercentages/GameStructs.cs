using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace gbfr.qol.detailedpercentages;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct ControllerEmParam
{
    [FieldOffset(0x1A0)]
    public ui_Component_ObjectRef Name;

    [FieldOffset(0x1C0)]
    public ui_Component_ObjectRef Level;

    [FieldOffset(0x1E0)]
    public ui_Component_ObjectRef HpValue;

    [FieldOffset(0x200)]
    public ui_Component_ObjectRef HpGauge;

    [FieldOffset(0x220)]
    public ui_Component_ObjectRef HpStatusGauge;

    [FieldOffset(0x240)]
    public ui_Component_ObjectRef HpStatusDamageGauge;

    [FieldOffset(0x260)]
    public ui_Component_ObjectRef ModeGauge;

    [FieldOffset(0x280)]
    public ui_Component_ObjectRef IconStatusSet;

    [FieldOffset(0x2A0)]
    public ui_Component_ObjectRef GaugeEffectObj;

    [FieldOffset(0x2C0)]
    public ui_Component_ObjectRef Root;

    [FieldOffset(0x338)]
    public Behavior* BehaviorEntity; // Example: Em1800 : EmBossBase : EmBehaviorBase : BehaviorAppBase : Behavior : cObj
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Behavior
{
    [FieldOffset(0x10)]
    public SubBehaviorObject* HpStuff; // This may look like a mere other behavior object too i.e Em1800::vftable but it's not, caused by all that BehaviorExtension stuff
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct SubBehaviorObject
{
    [FieldOffset(0x160)]
    public uint Health;

    [FieldOffset(0x164)]
    public uint HealthMax;
}

public unsafe struct ui_Component_ObjectRef
{
    public nint VTablePtr;
    public ui_Object* ObjectPtr;
    public nint Unk;
    public uint RefHash;
    public short RefObjectIndex;
    public short RefObjectId;
}

public unsafe struct ui_Object
{
    public nint VTablePtr;

    // Map?
    public uint field_0x08;
    public uint field_0x0C;
    public ui_Object** ChildrenBegin;
    public ui_Object** ChildrenEnd;
    public ui_Object** ChildrenCap;

}