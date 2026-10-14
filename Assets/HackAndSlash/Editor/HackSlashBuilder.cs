using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HackAndSlash;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the Void Arena scene from scratch: a rain-soaked skyscraper rooftop above a procedural night city, lit by
/// an HDRI sky, floodlights and neon, with wet planar reflections, photographic post-processing, the player rig and
/// the managers. Every visual is generated here or at runtime; see README for the CC0 texture sources.
/// </summary>
public static class HackSlashBuilder
{
    /// <summary>Bump whenever the generated scene changes; stale scenes are rebuilt automatically before Play.</summary>
    public const int Version = 2;

    const string Root = "Assets/HackAndSlash";
    const string ScenePath = Root + "/Scenes/VoidArena.unity";
    const string TexDir = Root + "/Textures/";
    const string EnvDir = Root + "/Environment/";
    const string RobotFbx = "Assets/SourceFiles/TimmyRobot/Models/TimmyRobot.fbx";
    const string RobotMat = "Assets/SourceFiles/TimmyRobot/Materials/TimmyRobot.mat";
    const string SrcController = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";

    const float RoofRadius = 18.3f;
    const float ParapetInner = 18.25f, ParapetOuter = 18.8f, ParapetHeight = 1.05f;
    const float TerraceRadius = 34f, TerraceY = -7f;
    const float Tile = 3.5f;

    static Shader lit, complexLit, sprite;
    static System.Random rng;

    // ---------------------------------------------------------------- menu

    [MenuItem("Hack & Slash/Play Void Arena", priority = 0)]
    public static void Play()
    {
        if (EditorApplication.isPlaying) return;
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            else EditorSceneManager.OpenScene(ScenePath);
        }
        if (IsStale()) Build();
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Hack & Slash/Rebuild Void Arena Scene", priority = 20)]
    public static void Build()
    {
        if (SceneManager.GetActiveScene().isDirty && SceneManager.GetActiveScene().path != ScenePath &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        rng = new System.Random(1977);
        AssetDatabase.Refresh();
        Folders();
        lit = Shader.Find("Universal Render Pipeline/Lit");
        complexLit = Shader.Find("Universal Render Pipeline/Complex Lit") ?? lit;
        sprite = Shader.Find("Sprites/Default");

        // ------------------------------------------------------------ textures
        var tRoofA = Tex("T_Roof_Albedo.jpg"); var tRoofN = Tex("T_Roof_Normal.jpg", normal: true);
        var tRoofMS = Tex("T_Roof_MetalSmooth.png", linear: true); var tRoofAO = Tex("T_Roof_AO.jpg", linear: true);
        var tPuddles = Tex("T_Roof_Puddles.png", linear: true);
        var tConA = Tex("T_Concrete_Albedo.jpg"); var tConN = Tex("T_Concrete_Normal.jpg", normal: true); var tConMS = Tex("T_Concrete_MetalSmooth.png", linear: true); var tConAO = Tex("T_Concrete_AO.jpg", linear: true);
        var tMetA = Tex("T_MetalPanel_Albedo.jpg"); var tMetN = Tex("T_MetalPanel_Normal.jpg", normal: true); var tMetMS = Tex("T_MetalPanel_MetalSmooth.png", linear: true); var tMetAO = Tex("T_MetalPanel_AO.jpg", linear: true);
        var tTreadA = Tex("T_TreadPlate_Albedo.jpg"); var tTreadN = Tex("T_TreadPlate_Normal.jpg", normal: true); var tTreadMS = Tex("T_TreadPlate_MetalSmooth.png", linear: true);
        var tFacA = Tex("T_Facade_Albedo.jpg"); var tFacE = Tex("T_Facade_Emission.jpg"); var tFacMS = Tex("T_Facade_MetalSmooth.png", linear: true);
        var tGrid = Tex("T_CityGrid_Emission.jpg");
        var tDirt = Tex("T_LensDirt.jpg");
        var tSpider = Tex("T_SpiderSense.png", alpha: true, maxSize: 512);
        var tRipple = Tex("T_Ripple.png", alpha: true, maxSize: 128);
        var tPanelN = Tex("T_ArmorPanel_Normal.png", normal: true, maxSize: 512);
        var tHeli = Tex("T_Helipad.png", alpha: true);
        var sky = Cube(EnvDir + "rooftop_night_2k.hdr");
        var samples = AssetDatabase.LoadAssetAtPath<TextAsset>(EnvDir + "HDRI_AmbientSamples.txt");

        // ------------------------------------------------------------ materials
        var glowTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceFiles/VFX/GlowTexture.png");
        var mGlow = SaveMat("M_FX_Glow", new Material(sprite) { mainTexture = glowTex, color = new Color(2.2f, 2.2f, 2.2f, 1f) });
        var mFlat = SaveMat("M_FX_Flat", new Material(sprite) { color = Color.white });
        var mSpider = SaveMat("M_FX_SpiderSense", new Material(sprite) { mainTexture = tSpider, color = Color.white });
        var mRipple = SaveMat("M_FX_Ripple", new Material(sprite) { mainTexture = tRipple, color = Color.white });

        var mRoof = SaveMat("M_Roof", PBR(lit, new Color(0.78f, 0.78f, 0.82f), tRoofA, tRoofN, 1f, tRoofMS, 1f, tRoofAO, Vector2.one, reflections: false));
        var mConcrete = SaveMat("M_Concrete", PBR(lit, new Color(0.62f, 0.6f, 0.62f), tConA, tConN, 1f, tConMS, 1f, tConAO, Vector2.one));
        var mMetalPanel = SaveMat("M_MetalPanel", PBR(lit, new Color(0.7f, 0.72f, 0.78f), tMetA, tMetN, 1f, tMetMS, 1f, tMetAO, Vector2.one));
        var mTread = SaveMat("M_TreadPlate", PBR(lit, new Color(0.55f, 0.55f, 0.6f), tTreadA, tTreadN, 1f, tTreadMS, 1f, null, Vector2.one));
        var mFacade = SaveMat("M_Facade", PBR(lit, Color.white, tFacA, null, 1f, tFacMS, 1f, null, Vector2.one, new Color(1.7f, 1.6f, 1.5f), tFacE));
        var mGrid = SaveMat("M_CityGrid", PBR(lit, Color.black, null, null, 1f, null, 0.2f, null, new Vector2(22f, 22f), new Color(2.4f, 2.2f, 2f), tGrid));
        mGrid.SetTexture("_BaseMap", tGrid); mGrid.SetTextureScale("_BaseMap", new Vector2(22f, 22f));
        var mHeli = PBR(lit, Color.white, tHeli, null, 1f, null, 0.82f, null, Vector2.one);
        Transparent(mHeli, 2920);
        mHeli = SaveMat("M_Helipad", mHeli);
        var mWet = SaveMat("M_WetReflection", WetMat(tPuddles, tRoofN));
        var mSky = SaveMat("M_NightSky", SkyMat(sky));
        var mBeam = SaveMat("M_LightBeam", BeamMat(new Color(0.85f, 0.92f, 1f), 0.16f));
        var mBeamNeon = SaveMat("M_LightBeamViolet", BeamMat(new Color(0.75f, 0.35f, 1f), 0.22f));
        var mMetal = SaveMat("M_Metal", PBR(lit, new Color(0.18f, 0.18f, 0.2f), null, null, 1f, null, 0.65f, null, Vector2.one, Color.black, null, 0.85f));
        var mBlade = SaveMat("M_VoidBlade", PBR(lit, new Color(0.3f, 0.8f, 1f), null, null, 1f, null, 0.9f, null, Vector2.one, new Color(0.5f, 2.3f, 4.8f)));
        var mDark = SaveMat("M_DarkMetal", PBR(lit, new Color(0.05f, 0.05f, 0.06f), null, null, 1f, null, 0.55f, null, Vector2.one, Color.black, null, 0.8f));
        var mLampFace = SaveMat("M_LampFace", PBR(lit, Color.white, null, null, 1f, null, 0.9f, null, Vector2.one, new Color(9f, 9.5f, 10f)));
        var mBeacon = SaveMat("M_Beacon", PBR(lit, new Color(0.3f, 0.02f, 0.02f), null, null, 1f, null, 0.8f, null, Vector2.one, new Color(4f, 0.15f, 0.05f)));
        var mNeonPink = SaveMat("M_NeonPink", PBR(lit, Color.black, null, null, 1f, null, 0.8f, null, Vector2.one, new Color(4.2f, 0.45f, 2.2f)));
        var mNeonCyan = SaveMat("M_NeonCyan", PBR(lit, Color.black, null, null, 1f, null, 0.8f, null, Vector2.one, new Color(0.35f, 2.4f, 4.4f)));
        var mNeonAmber = SaveMat("M_NeonAmber", PBR(lit, Color.black, null, null, 1f, null, 0.8f, null, Vector2.one, new Color(4.2f, 1.6f, 0.3f)));
        var mGlowRing = SaveMat("M_InnerGlow", PBR(lit, Color.black, null, null, 1f, null, 0.5f, null, Vector2.one, new Color(0.25f, 1.0f, 2.4f)));
        var mSpawn = SaveMat("M_SpawnGlow", PBR(lit, Color.black, null, null, 1f, null, 0.5f, null, Vector2.one, new Color(0.7f, 0.07f, 0.04f)));

        var armorPaint = new Material(complexLit) { name = "M_ArmorPaint" };
        armorPaint.SetColor("_BaseColor", Color.white);
        armorPaint.SetFloat("_Metallic", 0.05f);
        armorPaint.SetFloat("_Smoothness", 0.85f);
        if (tPanelN) { armorPaint.SetTexture("_BumpMap", tPanelN); armorPaint.SetFloat("_BumpScale", 0.7f); armorPaint.EnableKeyword("_NORMALMAP"); }
        armorPaint.SetFloat("_ClearCoat", 1f);
        armorPaint.SetFloat("_ClearCoatMask", 1f);
        armorPaint.SetFloat("_ClearCoatSmoothness", 0.94f);
        armorPaint.EnableKeyword("_CLEARCOAT");
        armorPaint.EnableKeyword("_EMISSION");
        armorPaint.SetColor("_EmissionColor", Color.black);
        var mArmor = SaveMat("M_ArmorPaint", armorPaint);

        var robotMat = AssetDatabase.LoadAssetAtPath<Material>(RobotMat);
        var suit = robotMat ? new Material(robotMat) : new Material(lit);
        suit.SetColor("_BaseColor", new Color(0.06f, 0.062f, 0.07f));
        suit.SetFloat("_Metallic", 0.6f);
        suit.SetFloat("_Smoothness", 0.55f);
        suit.EnableKeyword("_EMISSION");
        suit.SetColor("_EmissionColor", Color.black);
        var mSuit = SaveMat("M_Suit", suit);

        // ------------------------------------------------------------ animator (copy with IK pass enabled)
        string ctrlPath = Root + "/Animation/HS_Character.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) == null) AssetDatabase.CopyAsset(SrcController, ctrlPath);
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        var layers = ctrl.layers;
        layers[0].iKPass = true;
        ctrl.layers = layers;
        EditorUtility.SetDirty(ctrl);

        // ------------------------------------------------------------ meshes
        var floorMesh = SaveMesh(MeshKit.Disc(RoofRadius, 128, 24, Tile, "Mesh_Roof"));
        var parapet = SaveMesh(MeshKit.RingWall(ParapetInner, ParapetOuter, 0f, ParapetHeight, 160, 4f, "Mesh_Parapet"));
        var parapetCap = SaveMesh(MeshKit.RingWall(ParapetInner - 0.06f, ParapetOuter + 0.06f, ParapetHeight, ParapetHeight + 0.08f, 160, 2f, "Mesh_ParapetCap"));
        var towerTop = SaveMesh(MeshKit.CylinderSide(ParapetOuter, TerraceY, 0f, 96, 12f, 14f, "Mesh_TowerTop"));
        var terrace = SaveMesh(TerraceMesh());
        var terraceFacade = SaveMesh(MeshKit.CylinderSide(TerraceRadius, -190f, TerraceY, 128, 12f, 14f, "Mesh_TerraceFacade"));
        var ringGlow = SaveMesh(Annulus("Mesh_HelipadRing", 7.55f, 7.68f, 128, 1f));
        var spawnRing = SaveMesh(Annulus("Mesh_SpawnRing", 0.9f, 1.04f, 48, 1f));
        var beamMesh = SaveMesh(MeshKit.Cone(0.3f, 5.2f, 24f, 24, "Mesh_Floodbeam"));
        var skyBeamMesh = SaveMesh(MeshKit.Cone(1.2f, 22f, 420f, 24, "Mesh_Searchbeam"));
        var signBeamMesh = SaveMesh(MeshKit.Cone(0.2f, 3.5f, 60f, 16, "Mesh_SignBeam"));
        BuildCity(out var cityMesh, out var neonMeshes, out var beaconSpots, out var searchSpots);
        cityMesh = SaveMesh(cityMesh);
        for (int i = 0; i < neonMeshes.Length; i++) neonMeshes[i] = SaveMesh(neonMeshes[i]);

        // ------------------------------------------------------------ scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.skybox = mSky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 0.6f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0048f;
        RenderSettings.fogColor = new Color(0.12f, 0.08f, 0.125f); // matches the sky shader's city haze
        if (sky)
        {
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = sky;
        }
        RenderSettings.reflectionIntensity = 0.85f;

        var moonGo = new GameObject("Moonlight");
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.62f, 0.66f, 1f);
        moon.intensity = 0.38f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.75f;
        moonGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        RenderSettings.sun = moon;

        var boltGo = new GameObject("LightningLight");
        var bolt = boltGo.AddComponent<Light>();
        bolt.type = LightType.Directional;
        bolt.color = new Color(0.78f, 0.84f, 1f);
        bolt.intensity = 0f;
        bolt.shadows = LightShadows.None;
        boltGo.transform.rotation = Quaternion.Euler(60f, 120f, 0f);

        // ---- the roof
        var arena = new GameObject("Rooftop").transform;
        var floor = MeshObj("Roof", arena, floorMesh, mRoof, 0f, true);
        floor.layer = Layers.Floor;
        floor.AddComponent<MeshCollider>().sharedMesh = floorMesh;
        var wet = MeshObj("WetReflections", arena, floorMesh, mWet, 0.004f, false);
        wet.layer = Layers.Water;
        var heli = GameObject.CreatePrimitive(PrimitiveType.Quad);
        heli.name = "HelipadMarkings";
        Object.DestroyImmediate(heli.GetComponent<Collider>());
        heli.transform.SetParent(arena, false);
        heli.transform.localPosition = new Vector3(0f, 0.006f, 0f);
        heli.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        heli.transform.localScale = new Vector3(15.6f, 15.6f, 1f);
        heli.GetComponent<MeshRenderer>().sharedMaterial = mHeli;
        heli.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        heli.layer = Layers.Floor;
        var ring = MeshObj("HelipadRing", arena, ringGlow, mGlowRing, 0.008f, false);
        ring.layer = Layers.Floor;
        for (int i = 0; i < 24; i++)
        {
            var d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            var stud = Prim(PrimitiveType.Cylinder, "RunwayLight", arena, d * 8.3f + Vector3.up * 0.02f, new Vector3(0.14f, 0.02f, 0.14f), i % 2 == 0 ? mNeonAmber : mGlowRing, false);
            stud.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            stud.layer = Layers.Floor;
        }

        var wall = MeshObj("Parapet", arena, parapet, mConcrete, 0f, true);
        wall.layer = Layers.Environment;
        wall.AddComponent<MeshCollider>().sharedMesh = parapet;
        MeshObj("ParapetCap", arena, parapetCap, mMetalPanel, 0f, true).layer = Layers.Environment;
        BuildRailing(arena, mMetalPanel);
        MeshObj("TowerTop", arena, towerTop, mFacade, 0f, true).layer = Layers.Environment;
        var terr = MeshObj("Terrace", arena, terrace, mRoof, 0f, true);
        terr.layer = Layers.Environment;
        MeshObj("TerraceFacade", arena, terraceFacade, mFacade, 0f, false).layer = Layers.Environment;
        BuildTerraceProps(arena, mMetalPanel, mConcrete, mDark, mTread, mBeacon, mGlow, out var propBeacons);
        BuildFloodlights(arena, mDark, mLampFace, mBeam, beamMesh);
        BuildBillboard(arena, mDark, mNeonPink, mNeonCyan, mBeamNeon, signBeamMesh);

        // ---- the city
        var city = new GameObject("City").transform;
        MeshObj("Skyline", city, cityMesh, mFacade, 0f, false).layer = Layers.Environment;
        var neonMats = new[] { mNeonPink, mNeonCyan, mNeonAmber };
        for (int i = 0; i < neonMeshes.Length; i++) MeshObj("Neon_" + i, city, neonMeshes[i], neonMats[i % neonMats.Length], 0f, false);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "StreetGrid";
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.transform.SetParent(city, false);
        ground.transform.localPosition = new Vector3(0f, -190f, 0f);
        ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localScale = new Vector3(1400f, 1400f, 1f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = mGrid;
        ground.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var beacons = new List<Renderer>(propBeacons);
        foreach (var p in beaconSpots)
        {
            var b = Prim(PrimitiveType.Sphere, "Beacon", city, p, Vector3.one * 1.1f, mBeacon, false);
            b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            beacons.Add(b.GetComponent<MeshRenderer>());
        }
        foreach (var p in searchSpots)
        {
            var s = new GameObject("Searchlight").transform;
            s.SetParent(city, false);
            s.position = p;
            s.rotation = Quaternion.Euler(-70f - Rand(0f, 12f), Rand(0f, 360f), 0f);
            MeshObj("Beam", s, skyBeamMesh, mBeam, 0f, false).transform.localScale = Vector3.one;
            var sw = s.gameObject.AddComponent<Sweeper>();
            sw.yawAmplitude = Rand(25f, 50f);
            sw.yawSpeed = Rand(0.03f, 0.07f);
            sw.pitchAmplitude = Rand(5f, 12f);
        }
        AirTraffic(city, mGlow);
        SetLayerRecursive(arena, Layers.Environment, Layers.Floor, Layers.Water);
        SetLayerRecursive(city, Layers.Environment, Layers.Floor, Layers.Water);

        // spawn landing pads
        var spawnRoot = new GameObject("SpawnPoints").transform;
        var spawnPoints = new List<Transform>();
        for (int i = 0; i < 8; i++)
        {
            var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            var sp = new GameObject("Spawn_" + i).transform;
            sp.SetParent(spawnRoot, false);
            sp.position = dir * 12f;
            MeshObj("LandingMark", sp, spawnRing, mSpawn, 0.012f, false).layer = Layers.Floor;
            spawnPoints.Add(sp);
        }

        // reflection probe (rendered once at runtime by ArenaEnvironment)
        var probeGo = new GameObject("ReflectionProbe");
        probeGo.transform.position = new Vector3(0f, 3f, 0f);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(44f, 16f, 44f);
        probe.boxProjection = true;
        probe.resolution = 256;
        probe.hdr = true;
        probe.cullingMask = (1 << Layers.Environment) | (1 << Layers.Floor); // static set only: no player blob in reflections
        probe.importance = 1;

        // ---- post-processing
        var profile = NewProfile("VP_VoidArena");
        var bloom = AddVC<Bloom>(profile);
        bloom.threshold.value = 1.0f; bloom.intensity.value = 0.9f; bloom.scatter.value = 0.74f; bloom.highQualityFiltering.value = true;
        if (tDirt) { bloom.dirtTexture.value = tDirt; bloom.dirtIntensity.value = 2.2f; }
        var tone = AddVC<Tonemapping>(profile);
        tone.mode.value = TonemappingMode.ACES;
        var color = AddVC<ColorAdjustments>(profile);
        color.postExposure.value = 0.55f; color.contrast.value = 14f; color.saturation.value = 6f;
        var wb = AddVC<WhiteBalance>(profile);
        wb.temperature.value = -6f; wb.tint.value = 4f;
        var split = AddVC<SplitToning>(profile);
        split.shadows.value = new Color(0.36f, 0.46f, 0.62f); split.highlights.value = new Color(0.66f, 0.56f, 0.46f); split.balance.value = -15f;
        var vig = AddVC<Vignette>(profile);
        vig.intensity.value = 0.33f; vig.smoothness.value = 0.42f; vig.color.value = new Color(0.02f, 0f, 0.04f);
        var chroma = AddVC<ChromaticAberration>(profile);
        chroma.intensity.value = 0.05f;
        var grain = AddVC<FilmGrain>(profile);
        grain.type.value = FilmGrainLookup.Medium1; grain.intensity.value = 0.14f;
        var blur = AddVC<MotionBlur>(profile);
        blur.mode.value = MotionBlurMode.CameraOnly; blur.quality.value = MotionBlurQuality.Medium; blur.intensity.value = 0.16f;
        var vol = new GameObject("Global Volume").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;
        vol.sharedProfile = profile;

        var hitProfile = NewProfile("VP_HitPulse");
        AddVC<ChromaticAberration>(hitProfile).intensity.value = 1f;
        AddVC<LensDistortion>(hitProfile).intensity.value = -0.3f;
        var hitColor = AddVC<ColorAdjustments>(hitProfile);
        hitColor.postExposure.value = 0.35f; hitColor.saturation.value = 25f;
        var hitVol = new GameObject("Hit Pulse Volume").AddComponent<Volume>();
        hitVol.isGlobal = true;
        hitVol.priority = 10f;
        hitVol.weight = 0f;
        hitVol.sharedProfile = hitProfile;

        var cineProfile = NewProfile("VP_Cinematic");
        var dof = AddVC<DepthOfField>(cineProfile);
        dof.mode.value = DepthOfFieldMode.Bokeh; dof.focusDistance.value = 4.4f; dof.aperture.value = 2.2f; dof.focalLength.value = 85f; dof.bladeCount.value = 6;
        var cineVig = AddVC<Vignette>(cineProfile);
        cineVig.intensity.value = 0.48f; cineVig.smoothness.value = 0.5f;
        var cineColor = AddVC<ColorAdjustments>(cineProfile);
        cineColor.contrast.value = 22f; cineColor.saturation.value = -8f;
        var cineVol = new GameObject("Cinematic Volume").AddComponent<Volume>();
        cineVol.isGlobal = true;
        cineVol.priority = 20f;
        cineVol.weight = 0f;
        cineVol.sharedProfile = cineProfile;

        // ---- player
        var player = new GameObject("Player");
        var cc = player.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 0.97f, 0f);
        cc.height = 1.9f;
        cc.radius = 0.35f;
        cc.stepOffset = 0.35f;
        cc.skinWidth = 0.05f;
        cc.minMoveDistance = 0f;
        var rig = player.AddComponent<WeaponRig>();
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx), player.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        var anim = model.GetComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        smr.sharedMaterial = mSuit;
        var ik = model.AddComponent<IKRelay>();
        ik.rig = rig;
        ik.isPlayer = true;
        var pc = player.AddComponent<PlayerController>();
        pc.animator = anim;
        pc.model = model.transform;
        pc.rig = rig;
        pc.body = smr;
        pc.bladeMaterial = mBlade;
        var heroLight = new GameObject("HeroLight").AddComponent<Light>();
        heroLight.transform.SetParent(player.transform, false);
        heroLight.transform.localPosition = new Vector3(0f, 2.3f, 0.4f);
        heroLight.type = LightType.Point;
        heroLight.color = new Color(0.45f, 0.8f, 1f);
        heroLight.range = 5f;
        heroLight.intensity = 1.6f;
        heroLight.shadows = LightShadows.None;

        // ---- camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 52f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1400f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.antialiasingQuality = AntialiasingQuality.High;
        camData.stopNaN = true;
        camData.dithering = true;
        var camRig = camGo.AddComponent<CameraRig>();
        camRig.target = player.transform;
        camRig.yaw = 0f;
        var camRot = Quaternion.Euler(camRig.pitch, camRig.yaw, 0f);
        camGo.transform.SetPositionAndRotation(Vector3.up * 1.35f - camRot * Vector3.forward * camRig.distance, camRot);
        camGo.AddComponent<PlanarReflection>();

        // ---- managers & world systems
        var envGo = new GameObject("Environment");
        var env = envGo.AddComponent<ArenaEnvironment>();
        env.ambientSamples = samples;
        env.probe = probe;
        env.lightningLight = bolt;
        env.skyMaterial = mSky;
        env.beacons = beacons.ToArray();
        var weather = envGo.AddComponent<Weather>();
        weather.streakMaterial = mGlow;
        weather.rippleMaterial = mRipple;
        weather.roofRadius = RoofRadius - 0.6f;

        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gmGo.AddComponent<Sfx>();
        var hud = new GameObject("HUD").AddComponent<HUD>();
        gm.player = pc;
        gm.cameraRig = camRig;
        gm.hud = hud;
        gm.hitVolume = hitVol;
        gm.cinematicVolume = cineVol;
        gm.environment = env;
        gm.builderVersion = Version;
        gm.spawnPoints = spawnPoints.ToArray();
        gm.robotModel = AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx);
        gm.characterController = ctrl;
        gm.robotMaterial = robotMat;
        gm.suitMaterial = mSuit;
        gm.armorPaintMaterial = mArmor;
        gm.glowMaterial = mGlow;
        gm.trailMaterial = mFlat;
        gm.metalMaterial = mMetal;
        gm.spiderSenseMaterial = mSpider;
        gm.music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SourceFiles/SoundFX/Music_Exciting.ogg");
        gm.pickupClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SourceFiles/SoundFX/SFX_PositiveSound01.ogg");
        var steps = new List<AudioClip>();
        for (int i = 1; i <= 10; i++)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SourceFiles/TimmyRobot/Sfx/Player_Footstep_" + i.ToString("00") + ".wav");
            if (c) steps.Add(c);
        }
        gm.footsteps = steps.ToArray();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player;
        if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("[Hack & Slash] Void Arena (builder v" + Version + ") built at " + ScenePath + ". Press Play (or Hack & Slash > Play Void Arena).");
    }

    // ---------------------------------------------------------------- stale-scene guard

    /// <summary>True when the saved scene was generated by an older builder (its GameManager.builderVersion is lower).</summary>
    public static bool IsStale()
    {
        if (!File.Exists(ScenePath)) return true;
        try
        {
            var m = Regex.Match(File.ReadAllText(ScenePath), @"builderVersion:\s*(\d+)");
            return !m.Success || int.Parse(m.Groups[1].Value) < Version;
        }
        catch (IOException) { return false; }
    }

    [InitializeOnLoadMethod]
    static void HookPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingEditMode) return;
        if (SceneManager.GetActiveScene().path != ScenePath || !IsStale()) return;
        // the committed scene predates this version of the game: regenerate it, then enter Play mode
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () =>
        {
            Build();
            EditorApplication.isPlaying = true;
        };
    }

    static void AddToBuildSettings()
    {
        var list = new List<EditorBuildSettingsScene>();
        list.Add(new EditorBuildSettingsScene(ScenePath, true));
        var existing = EditorBuildSettings.scenes;
        if (existing != null)
            foreach (var s in existing)
                if (s != null && s.path != ScenePath) list.Add(s);
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ---------------------------------------------------------------- set pieces

    static Mesh TerraceMesh()
    {
        var b = new MeshKit.Builder();
        const int seg = 128;
        for (int s = 0; s < seg; s++)
        {
            float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s + 1) / (float)seg * Mathf.PI * 2f;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 p0 = d0 * ParapetOuter + Vector3.up * TerraceY, p1 = d1 * ParapetOuter + Vector3.up * TerraceY;
            Vector3 p2 = d1 * TerraceRadius + Vector3.up * TerraceY, p3 = d0 * TerraceRadius + Vector3.up * TerraceY;
            b.Quad(p0, p1, p2, p3, Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                new Vector2(p0.x, p0.z) / Tile, new Vector2(p1.x, p1.z) / Tile, new Vector2(p2.x, p2.z) / Tile, new Vector2(p3.x, p3.z) / Tile);
            // low lip around the terrace
            Vector3 q0 = d0 * TerraceRadius, q1 = d1 * TerraceRadius;
            b.Quad(q0 + Vector3.up * TerraceY, q1 + Vector3.up * TerraceY, q1 + Vector3.up * (TerraceY + 0.9f), q0 + Vector3.up * (TerraceY + 0.9f), -d0, -d1, -d1, -d0,
                new Vector2(s * 0.6f, 0f), new Vector2((s + 1) * 0.6f, 0f), new Vector2((s + 1) * 0.6f, 0.25f), new Vector2(s * 0.6f, 0.25f));
        }
        return b.ToMesh("Mesh_Terrace");
    }

    static void BuildRailing(Transform parent, Material metal)
    {
        var rail = new GameObject("Railing").transform;
        rail.SetParent(parent, false);
        const int posts = 48;
        float r = (ParapetInner + ParapetOuter) * 0.5f;
        for (int i = 0; i < posts; i++)
        {
            float a = i * 360f / posts;
            var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Prim(PrimitiveType.Cylinder, "Post", rail, d * r + Vector3.up * (ParapetHeight + 0.5f), new Vector3(0.06f, 0.5f, 0.06f), metal, false).layer = Layers.Environment;
            var d2 = Quaternion.Euler(0f, a + 360f / posts, 0f) * Vector3.forward;
            Vector3 mid = (d + d2) * 0.5f * r;
            float len = (d2 - d).magnitude * r;
            foreach (float y in new[] { ParapetHeight + 0.55f, ParapetHeight + 1.0f })
            {
                var bar = Prim(PrimitiveType.Cube, "Rail", rail, mid + Vector3.up * y, new Vector3(0.05f, 0.05f, len + 0.02f), metal, false);
                bar.transform.localRotation = Quaternion.LookRotation(d2 - d);
                bar.layer = Layers.Environment;
            }
        }
    }

    static void BuildTerraceProps(Transform parent, Material panel, Material concrete, Material dark, Material tread, Material beacon, Material glow, out List<Renderer> beacons)
    {
        beacons = new List<Renderer>();
        var props = new GameObject("TerraceProps").transform;
        props.SetParent(parent, false);
        float y = TerraceY;
        for (int i = 0; i < 10; i++)
        {
            float a = i * 36f + 9f + Rand(-6f, 6f);
            var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            var rot = Quaternion.LookRotation(-d);
            float r = Rand(22f, 29f);
            int kind = i % 5;
            var p = new GameObject("Prop_" + i).transform;
            p.SetParent(props, false);
            p.SetPositionAndRotation(d * r + Vector3.up * y, rot);
            switch (kind)
            {
                case 0: // HVAC block with fans
                {
                    Plate(p, new Vector3(0f, 1.1f, 0f), new Vector3(4.2f, 2.2f, 2.6f), 0.08f, panel);
                    for (int f = -1; f <= 1; f += 2)
                    {
                        Plate(p, new Vector3(f * 1.05f, 2.25f, 0f), new Vector3(1.5f, 0.12f, 1.5f), 0.05f, dark);
                        Prim(PrimitiveType.Cylinder, "Fan", p, new Vector3(f * 1.05f, 2.33f, 0f), new Vector3(1.25f, 0.02f, 1.25f), tread, false).AddComponent<Spinner>().degreesPerSecond = Rand(140f, 260f);
                    }
                    Steam(p, new Vector3(1.6f, 2.3f, 0.9f), glow);
                    break;
                }
                case 1: // water tank on stilts
                {
                    for (int l = 0; l < 4; l++)
                        Prim(PrimitiveType.Cylinder, "Leg", p, new Vector3(l < 2 ? -1.2f : 1.2f, 1.5f, l % 2 == 0 ? -1.2f : 1.2f), new Vector3(0.18f, 1.5f, 0.18f), dark, false);
                    Prim(PrimitiveType.Cylinder, "Tank", p, new Vector3(0f, 5f, 0f), new Vector3(3.6f, 2f, 3.6f), panel, false);
                    Prim(PrimitiveType.Cylinder, "Lid", p, new Vector3(0f, 7.1f, 0f), new Vector3(3.8f, 0.1f, 3.8f), dark, false);
                    break;
                }
                case 2: // antenna mast with aircraft beacon
                {
                    float h = Rand(12f, 20f);
                    Prim(PrimitiveType.Cylinder, "Mast", p, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.25f, h * 0.5f, 0.25f), panel, false);
                    for (int k = 1; k < 4; k++) Prim(PrimitiveType.Cube, "Strut", p, new Vector3(0f, h * k / 4f, 0f), new Vector3(1.4f, 0.06f, 0.06f), dark, false);
                    var b = Prim(PrimitiveType.Sphere, "Beacon", p, new Vector3(0f, h + 0.25f, 0f), Vector3.one * 0.5f, beacon, false);
                    b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    beacons.Add(b.GetComponent<MeshRenderer>());
                    break;
                }
                case 3: // vent stacks
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 o = new Vector3(k * 1.3f - 1.3f, 0f, Rand(-0.6f, 0.6f));
                        float h = Rand(2.2f, 3.6f);
                        Prim(PrimitiveType.Cylinder, "Stack", p, o + Vector3.up * (h * 0.5f), new Vector3(0.7f, h * 0.5f, 0.7f), panel, false);
                        Steam(p, o + Vector3.up * (h + 0.1f), glow);
                    }
                    break;
                }
                default: // service hut
                {
                    Plate(p, new Vector3(0f, 1.6f, 0f), new Vector3(5f, 3.2f, 3.8f), 0.06f, concrete);
                    Plate(p, new Vector3(0f, 3.3f, 0f), new Vector3(5.3f, 0.2f, 4.1f), 0.04f, dark);
                    Plate(p, new Vector3(0f, 1.2f, 1.92f), new Vector3(1.2f, 2.3f, 0.06f), 0.02f, dark);
                    var lamp = Plate(p, new Vector3(0f, 2.65f, 1.98f), new Vector3(0.6f, 0.1f, 0.12f), 0.03f, glow);
                    lamp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    var l = new GameObject("DoorLight").AddComponent<Light>();
                    l.transform.SetParent(p, false);
                    l.transform.localPosition = new Vector3(0f, 2.5f, 2.6f);
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.75f, 0.45f);
                    l.range = 7f;
                    l.intensity = 6f;
                    l.shadows = LightShadows.None;
                    break;
                }
            }
        }
    }

    static void Steam(Transform parent, Vector3 localPos, Material mat)
    {
        var go = new GameObject("Steam");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.55f, 0.62f, 0.09f), new Color(0.7f, 0.7f, 0.8f, 0.16f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;
        var em = ps.emission;
        em.rateOverTime = 7f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 12f;
        sh.radius = 0.3f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.6f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-1.2f, -0.4f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0.2f);
        vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static void BuildFloodlights(Transform parent, Material dark, Material lamp, Material beamMat, Mesh beamMesh)
    {
        var root = new GameObject("Floodlights").transform;
        root.SetParent(parent, false);
        for (int i = 0; i < 4; i++)
        {
            float a = 22.5f + i * 90f;
            var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            var pole = new GameObject("Floodlight_" + i).transform;
            pole.SetParent(root, false);
            pole.position = d * 17.4f;
            pole.rotation = Quaternion.LookRotation(-d);
            var shaft = Prim(PrimitiveType.Cylinder, "Pole", pole, new Vector3(0f, 3.4f, 0f), new Vector3(0.24f, 3.4f, 0.24f), dark, true);
            shaft.layer = Layers.Environment;
            Prim(PrimitiveType.Cube, "Base", pole, new Vector3(0f, 0.15f, 0f), new Vector3(0.8f, 0.3f, 0.8f), dark, false).layer = Layers.Environment;
            var head = new GameObject("Head").transform;
            head.SetParent(pole, false);
            head.localPosition = new Vector3(0f, 6.9f, 0.25f);
            Vector3 aim = -d * 1.5f - head.position; // just past the centre of the helipad
            head.rotation = Quaternion.LookRotation(aim.normalized);
            Plate(head, new Vector3(0f, 0f, -0.15f), new Vector3(1.1f, 0.62f, 0.35f), 0.06f, dark);
            var face = Plate(head, new Vector3(0f, 0f, 0.03f), new Vector3(0.95f, 0.48f, 0.04f), 0.02f, lamp);
            face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var spot = head.gameObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(0.86f, 0.92f, 1f);
            spot.range = 40f;
            spot.spotAngle = 62f;
            spot.innerSpotAngle = 30f;
            spot.intensity = i % 2 == 0 ? 160f : 120f;
            spot.shadows = i % 2 == 0 ? LightShadows.Soft : LightShadows.None;
            spot.shadowStrength = 0.85f;
            var beam = MeshObj("Beam", head, beamMesh, beamMat, 0f, false);
            beam.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            beam.transform.localRotation = Quaternion.identity;
        }
    }

    static void BuildBillboard(Transform parent, Material dark, Material pink, Material cyan, Material beamMat, Mesh beamMesh)
    {
        var root = new GameObject("NeonBillboard").transform;
        root.SetParent(parent, false);
        var d = Vector3.forward;
        root.position = d * 23.5f + Vector3.up * (TerraceY + 7.5f);
        root.rotation = Quaternion.LookRotation(-d);
        Plate(root, Vector3.zero, new Vector3(16f, 6f, 0.4f), 0.12f, dark);
        for (int i = -1; i <= 1; i += 2)
            Prim(PrimitiveType.Cylinder, "Strut", root, new Vector3(i * 5.5f, -5f, -0.3f), new Vector3(0.35f, 4.5f, 0.35f), dark, false);
        // local +Z faces the arena: tubes, text and spill lights go on that side, struts and beams behind
        var frame = new[] { (new Vector3(0f, 2.9f, 0.25f), new Vector3(15.6f, 0.12f, 0.12f)), (new Vector3(0f, -2.9f, 0.25f), new Vector3(15.6f, 0.12f, 0.12f)),
                            (new Vector3(7.8f, 0f, 0.25f), new Vector3(0.12f, 5.9f, 0.12f)), (new Vector3(-7.8f, 0f, 0.25f), new Vector3(0.12f, 5.9f, 0.12f)) };
        foreach (var (pos, size) in frame)
        {
            var tube = Plate(root, pos, size, 0.05f, pink);
            tube.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            tube.AddComponent<NeonFlicker>().emission = new Color(4.2f, 0.45f, 2.2f);
        }
        var inner = Plate(root, new Vector3(0f, -1.85f, 0.25f), new Vector3(11f, 0.08f, 0.08f), 0.03f, cyan);
        inner.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var textGo = new GameObject("NeonText");
        textGo.transform.SetParent(root, false);
        textGo.transform.localPosition = new Vector3(0f, 0.35f, 0.3f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tmp = textGo.AddComponent<TextMeshPro>();
        tmp.text = "VOID ARENA";
        tmp.fontSize = 42f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.characterSpacing = 18f;
        tmp.rectTransform.sizeDelta = new Vector2(15f, 4f);
        if (tmp.font != null)
        {
            var neonText = new Material(tmp.font.material) { name = "M_NeonText" };
            neonText.SetColor(ShaderUtilities.ID_FaceColor, new Color(4.5f, 1.4f, 3.2f, 1f));
            neonText.EnableKeyword(ShaderUtilities.Keyword_Glow);
            neonText.SetColor(ShaderUtilities.ID_GlowColor, new Color(1f, 0.25f, 0.8f, 0.6f));
            neonText.SetFloat(ShaderUtilities.ID_GlowPower, 0.6f);
            neonText.SetFloat(ShaderUtilities.ID_GlowOuter, 0.4f);
            tmp.fontSharedMaterial = SaveMat("M_NeonText", neonText);
        }
        tmp.color = Color.white;

        var sub = new GameObject("NeonSub");
        sub.transform.SetParent(root, false);
        sub.transform.localPosition = new Vector3(0f, -2.3f, 0.3f);
        sub.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var t2 = sub.AddComponent<TextMeshPro>();
        t2.text = "ROBOTICS  ·  DEMOLITION  ·  24/7";
        t2.fontSize = 12f;
        t2.alignment = TextAlignmentOptions.Center;
        t2.characterSpacing = 12f;
        t2.color = new Color(0.55f, 0.95f, 1f);
        t2.rectTransform.sizeDelta = new Vector2(15f, 1.2f);

        foreach (var x in new[] { -5f, 0f, 5f })
        {
            var l = new GameObject("NeonSpill").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.transform.localPosition = new Vector3(x, -1f, 2.5f);
            l.type = LightType.Point;
            l.color = x == 0f ? new Color(0.35f, 0.85f, 1f) : new Color(1f, 0.25f, 0.7f);
            l.range = 20f;
            l.intensity = 34f;
            l.shadows = LightShadows.None;
        }
        // two violet beams thrown up behind the sign
        for (int i = -1; i <= 1; i += 2)
        {
            var b = new GameObject("SkyBeam").transform;
            b.SetParent(root, false);
            b.localPosition = new Vector3(i * 6f, -3f, -1.2f);
            b.localRotation = Quaternion.Euler(-78f, i * 14f, 0f);
            MeshObj("Beam", b, beamMesh, beamMat, 0f, false);
            var sw = b.gameObject.AddComponent<Sweeper>();
            sw.yawAmplitude = 18f; sw.yawSpeed = 0.11f; sw.pitchAmplitude = 6f;
        }
    }

    /// <summary>Procedural night skyline: towers with lit windows (world-scale UVs), neon strips, beacons and searchlights.</summary>
    static void BuildCity(out Mesh city, out Mesh[] neon, out List<Vector3> beacons, out List<Vector3> searchlights)
    {
        var b = new MeshKit.Builder();
        var neonB = new[] { new MeshKit.Builder(), new MeshKit.Builder(), new MeshKit.Builder() };
        beacons = new List<Vector3>();
        searchlights = new List<Vector3>();
        const float baseY = -190f;
        int count = 0;
        for (int ringI = 0; ringI < 4; ringI++)
        {
            float rMin = 48f + ringI * 55f, rMax = rMin + 50f;
            int n = 14 + ringI * 8;
            for (int i = 0; i < n; i++)
            {
                float a = (i + Rand(-0.35f, 0.35f)) / n * 360f;
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float r = Rand(rMin, rMax);
                float w = Rand(14f, 30f) * (1f + ringI * 0.2f), dpt = Rand(14f, 30f) * (1f + ringI * 0.2f);
                float top = Rand(-45f, 40f) + (ringI >= 1 ? Rand(0f, 110f) : 0f) + (Rand(0f, 1f) < 0.15f ? 80f : 0f);
                float h = top - baseY;
                Vector3 c = d * r + Vector3.up * (baseY + h * 0.5f);
                float yaw = a + Rand(-20f, 20f);
                b.Box(c, new Vector3(w, h, dpt), yaw, new Vector2(12f, 14f), new Vector2(Mathf.Floor(Rand(0f, 4f)) * 0.25f, Mathf.Floor(Rand(0f, 4f)) * 0.25f));
                // setback crown on taller towers
                if (top > 40f && Rand(0f, 1f) < 0.6f)
                {
                    float cw = w * Rand(0.5f, 0.75f), cd = dpt * Rand(0.5f, 0.75f), ch = Rand(10f, 30f);
                    b.Box(d * r + Vector3.up * (top + ch * 0.5f), new Vector3(cw, ch, cd), yaw, new Vector2(12f, 14f), Vector2.zero);
                    top += ch;
                }
                if (top > 20f && Rand(0f, 1f) < 0.55f) beacons.Add(d * r + Vector3.up * (top + 1.2f));
                if (Rand(0f, 1f) < 0.45f)
                {
                    // neon strips: vertical edge lights or a horizontal band near the top
                    var nb = neonB[count % 3];
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    if (Rand(0f, 1f) < 0.5f)
                    {
                        Vector3 corner = d * r + rot * new Vector3(w * 0.5f + 0.15f, 0f, -dpt * 0.5f - 0.15f);
                        nb.Box(new Vector3(corner.x, top - 30f, corner.z), new Vector3(0.5f, 60f, 0.5f), yaw, new Vector2(1f, 1f), Vector2.zero);
                    }
                    else
                    {
                        nb.Box(d * r + Vector3.up * (top - Rand(4f, 10f)), new Vector3(w + 0.6f, 1.2f, dpt + 0.6f), yaw, new Vector2(1f, 1f), Vector2.zero, false);
                    }
                }
                if (ringI >= 1 && top > 60f && searchlights.Count < 5 && Rand(0f, 1f) < 0.3f) searchlights.Add(d * r + Vector3.up * (top + 2f));
                count++;
            }
        }
        city = b.ToMesh("Mesh_Skyline");
        neon = new[] { neonB[0].ToMesh("Mesh_NeonPink"), neonB[1].ToMesh("Mesh_NeonCyan"), neonB[2].ToMesh("Mesh_NeonAmber") };
    }

    static void AirTraffic(Transform parent, Material glow)
    {
        var go = new GameObject("AirTraffic");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 70f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 10f;
        main.startLifetime = 40f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.85f, 0.9f), new Color(1f, 0.3f, 0.2f, 0.9f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        var em = ps.emission;
        em.rateOverTime = 2.5f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(40f, 50f, 700f);
        sh.position = new Vector3(-320f, 0f, 0f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(12f, 22f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        vel.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = glow;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // ---------------------------------------------------------------- helpers

    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Puts set dressing on the Environment layer (camera collision, debris, reflection probe), keeping special layers.</summary>
    static void SetLayerRecursive(Transform t, int layer, params int[] keep)
    {
        if (System.Array.IndexOf(keep, t.gameObject.layer) < 0) t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayerRecursive(c, layer, keep);
    }

    static void Folders()
    {
        foreach (var f in new[] { "Materials", "Meshes", "Scenes", "Settings", "Animation", "Textures", "Environment" })
            if (!AssetDatabase.IsValidFolder(Root + "/" + f)) AssetDatabase.CreateFolder(Root, f);
    }

    /// <summary>Loads a texture and fixes its import settings (normal map / linear / alpha) if needed.</summary>
    static Texture2D Tex(string file, bool normal = false, bool linear = false, bool alpha = false, int maxSize = 2048)
    {
        string path = TexDir + file;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Debug.LogWarning("[Hack & Slash] Missing texture " + path); return null; }
        bool dirty = false;
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (imp.textureType != type) { imp.textureType = type; dirty = true; }
        bool srgb = !(normal || linear);
        if (!normal && imp.sRGBTexture != srgb) { imp.sRGBTexture = srgb; dirty = true; }
        if (alpha && !imp.alphaIsTransparency) { imp.alphaIsTransparency = true; imp.alphaSource = TextureImporterAlphaSource.FromInput; dirty = true; }
        if (imp.maxTextureSize != maxSize) { imp.maxTextureSize = maxSize; dirty = true; }
        if (imp.anisoLevel < 4 && !alpha) { imp.anisoLevel = 8; dirty = true; }
        if (dirty) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Cubemap Cube(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Debug.LogWarning("[Hack & Slash] Missing HDRI " + path); return null; }
        if (imp.textureShape != TextureImporterShape.TextureCube || !imp.mipmapEnabled || imp.maxTextureSize != 2048)
        {
            imp.textureShape = TextureImporterShape.TextureCube;
            imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    static Material PBR(Shader shader, Color tint, Texture2D albedo, Texture2D normal, float normalScale, Texture2D metalSmooth, float smoothness, Texture2D ao, Vector2 tiling,
        Color? emission = null, Texture2D emissionMap = null, float metallic = 0f, bool reflections = true)
    {
        var m = new Material(shader);
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (albedo) m.SetTexture("_BaseMap", albedo);
        m.SetTextureScale("_BaseMap", tiling);
        if (normal)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", normalScale);
            m.EnableKeyword("_NORMALMAP");
        }
        if (metalSmooth)
        {
            m.SetTexture("_MetallicGlossMap", metalSmooth);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        if (ao)
        {
            m.SetTexture("_OcclusionMap", ao);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
        }
        if (emission.HasValue && emission.Value.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            if (emissionMap) m.SetTexture("_EmissionMap", emissionMap);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (!reflections)
        {
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }
        return m;
    }

    static void Transparent(Material m, int queue)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = queue;
    }

    static Material WetMat(Texture2D puddles, Texture2D normal)
    {
        var sh = Shader.Find("VoidArena/WetReflection");
        var m = new Material(sh ? sh : sprite);
        if (puddles) m.SetTexture("_MaskTex", puddles);
        if (normal) m.SetTexture("_NormalTex", normal);
        m.SetFloat("_Tile", Tile);
        m.SetFloat("_Strength", 0.95f);
        m.SetFloat("_DampReflect", 0.2f);
        m.SetFloat("_MinFresnel", 0.35f);
        m.SetFloat("_Distortion", 0.012f);
        m.SetFloat("_Blur", 1.2f);
        return m;
    }

    static Material SkyMat(Cubemap cube)
    {
        var sh = Shader.Find("VoidArena/NightSky");
        var m = new Material(sh ? sh : Shader.Find("Skybox/Cubemap"));
        if (cube) m.SetTexture("_Tex", cube);
        m.SetColor("_Tint", new Color(0.92f, 0.9f, 1f));
        m.SetFloat("_Exposure", 0.55f);
        m.SetFloat("_Rotation", 200f);
        m.SetColor("_HorizonColor", new Color(0.13f, 0.075f, 0.14f));
        m.SetFloat("_Flash", 0f);
        return m;
    }

    static Material BeamMat(Color c, float intensity)
    {
        var sh = Shader.Find("VoidArena/LightBeam");
        var m = new Material(sh ? sh : sprite);
        m.SetColor("_Color", c);
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_EdgePower", 1.6f);
        return m;
    }

    static Material SaveMat(string name, Material m)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing)
        {
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            existing.globalIlluminationFlags = m.globalIlluminationFlags;
            existing.renderQueue = m.renderQueue;
            existing.SetOverrideTag("RenderType", m.GetTag("RenderType", false, ""));
            existing.SetShaderPassEnabled("DepthOnly", m.GetShaderPassEnabled("DepthOnly"));
            existing.SetShaderPassEnabled("ShadowCaster", m.GetShaderPassEnabled("ShadowCaster"));
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }
        m.name = name;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Mesh Annulus(string name, float inner, float outer, int seg, float uTiles)
    {
        var mesh = new Mesh { name = name };
        var v = new Vector3[(seg + 1) * 2];
        var n = new Vector3[v.Length];
        var uv = new Vector2[v.Length];
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i * 2] = d * inner;
            v[i * 2 + 1] = d * outer;
            n[i * 2] = n[i * 2 + 1] = Vector3.up;
            uv[i * 2] = new Vector2(i / (float)seg * uTiles, 0f);
            uv[i * 2 + 1] = new Vector2(i / (float)seg * uTiles, 1f);
        }
        var t = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            t[i * 6] = a; t[i * 6 + 1] = c; t[i * 6 + 2] = b;
            t[i * 6 + 3] = b; t[i * 6 + 4] = c; t[i * 6 + 5] = d;
        }
        mesh.vertices = v;
        mesh.normals = n;
        mesh.uv = uv;
        mesh.triangles = t;
        MeshKit.FixWinding(mesh);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    static Mesh SaveMesh(Mesh m)
    {
        string path = Root + "/Meshes/" + m.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            existing.Clear();
            existing.indexFormat = m.indexFormat;
            existing.vertices = m.vertices;
            existing.normals = m.normals;
            existing.uv = m.uv;
            existing.colors = m.colors;
            existing.triangles = m.triangles;
            existing.RecalculateBounds();
            existing.RecalculateTangents();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>Bevelled box saved as a mesh asset (shared by size).</summary>
    static GameObject Plate(Transform parent, Vector3 localPos, Vector3 size, float radius, Material mat)
    {
        string name = string.Format("Mesh_Plate_{0:F2}x{1:F2}x{2:F2}_{3:F2}", size.x, size.y, size.z, radius).Replace('.', 'p');
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/" + name + ".asset");
        if (mesh == null)
        {
            mesh = MeshKit.BuildRoundedBox(size, radius, 3, 1f, 1.5f);
            mesh.name = name;
            mesh = SaveMesh(mesh);
        }
        var go = new GameObject("Plate");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return go;
    }

    static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, float y, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    static VolumeProfile NewProfile(string name)
    {
        string path = Root + "/Settings/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);
        return p;
    }

    static T AddVC<T>(VolumeProfile p) where T : VolumeComponent
    {
        var c = p.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, p);
        EditorUtility.SetDirty(p);
        return c;
    }
}
