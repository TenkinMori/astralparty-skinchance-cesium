using CesiumLoader.SDK.Manifests;

[assembly: ModManifest(
    "战损立绘",
    "1.0.0",
    "TenkinMori",
    "按血量百分比切换局内左下角自己的角色立绘，做成战损效果；贴图与阈值写在配置里，可随时热重载。",
    Permissions = ModPermission.ReadGameState | ModPermission.ModifyGameState | ModPermission.FileSystem,
    SdkVersion = "3.8.1")]
