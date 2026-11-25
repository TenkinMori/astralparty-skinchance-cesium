using CesiumLoader.SDK.Manifests;

[assembly: ModManifest(
    "战损立绘",
    "1.3.5",
    "TenkinMori",
    "按血量切换局内左下角自己的角色立绘；每张皮肤一个文件夹，文件夹里的 config.json 可配阈值、贴图名（0=原图）、缩放与偏移，按 F12 可在游戏里直接拖动调整并保存。",
    Permissions = ModPermission.ReadGameState | ModPermission.ModifyGameState | ModPermission.FileSystem,
    SdkVersion = "3.8.1")]
