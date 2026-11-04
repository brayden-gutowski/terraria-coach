using Terraria;
using Terraria.ModLoader;
namespace TerrariaVoice;
public class TerrariaVoice : Mod
{
 internal static ModKeybind? MuteKey;
 public override void Load(){if(!Main.dedServ)MuteKey=KeybindLoader.RegisterKeybind(this,"Toggle coach mute","F8");}
 public override void Unload(){MuteKey=null;}
}
