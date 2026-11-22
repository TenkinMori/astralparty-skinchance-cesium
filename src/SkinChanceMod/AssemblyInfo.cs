using CesiumLoader.SDK.Manifests;

[assembly: ModManifest(
    "战损立绘",
    "1.1.0",
    "TenkinMori",
    "按血量百分比切换局内左下角自己的角色立绘，做成战损效果；全部皮肤与文件夹已预置好，只要把 PNG 放进 skins\\<皮肤>\\ 就能用。",
    Permissions = ModPermission.ReadGameState | ModPermission.ModifyGameState | ModPermission.FileSystem,
    SdkVersion = "3.8.1")]
