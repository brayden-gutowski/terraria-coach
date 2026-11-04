using System.ComponentModel;
using Terraria.ModLoader.Config;
namespace TerrariaVoice;
public class CoachConfig : ModConfig
{
 public override ConfigScope Mode=>ConfigScope.ClientSide;
 [DefaultValue("")] public string PairingToken {get;set;}="";
 [DefaultValue(true)] public bool Enabled {get;set;}=true;
}
