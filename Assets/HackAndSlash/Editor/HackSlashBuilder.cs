using System.Collections.Generic;
using HackAndSlash;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Builds the Void Arena scene (materials, meshes, lighting, post-processing, player, managers) from scratch.</summary>
public static class HackSlashBuilder
{
    const string Root = "Assets/HackAndSlash";
    const string ScenePath = Root + "/Scenes/VoidArena.unity";
    const string RobotFbx = "Assets/SourceFiles/TimmyRobot/Models/TimmyRobot.fbx";
    const string RobotMat = "Assets/SourceFiles/TimmyRobot/Materials/TimmyRobot.mat";
    const string SrcController = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    const string Tiles = "Assets/SourceFiles/Textures/Repeating Tiles/";

    static Shader lit, sprite;

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
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Hack & Slash/Rebuild Void Arena Scene", priority = 20)]
    public static void Build()
    {
        if (SceneManager.GetActiveScene().isDirty && SceneManager.GetActiveScene().path != ScenePath &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Folders();
        lit = Shader.Find("Universal Render Pipeline/Lit");
        sprite = Shader.Find("Sprites/Default");

        // ------------------------------------------------------------ materials
        var glowTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceFiles/VFX/GlowTexture.png");
        var mGlow = SaveMat("M_FX_Glow", new Material(sprite) { mainTexture = glowTex, color = new Color(2.2f, 2.2f, 2.2f, 1f) });
        var mFlat = SaveMat("M_FX_Flat", new Material(sprite) { color = Color.white });

        var mFloor = SaveMat("M_Floor", Lit(new Color(0.34f, 0.32f, 0.42f), 0.25f, 0.5f,
            Tex("Tile_Hexagon_Grey_Albedo.png"), new Vector2(9f, 9f), Normal("Tile_Hexagon_normal.png")));
        var mStone = SaveMat("M_Stone", Lit(new Color(0.22f, 0.2f, 0.27f), 0.05f, 0.28f));
        var mStoneDark = SaveMat("M_StoneDark", Lit(new Color(0.055f, 0.05f, 0.07f), 0f, 0.15f));
        var mRunes = SaveMat("M_RuneBand", Lit(new Color(0.025f, 0.02f, 0.035f), 0.3f, 0.6f, null, null, null,
            new Color(1.7f, 0.8f, 2.8f), Tex("Runes_Emission.png")));
        var mEdge = SaveMat("M_EdgeGlow", Lit(Color.black, 0f, 0.5f, null, null, null, new Color(1.1f, 0.35f, 2.6f)));
        var mInner = SaveMat("M_InnerGlow", Lit(Color.black, 0f, 0.5f, null, null, null, new Color(0.3f, 1.2f, 2.8f)));
        var mSpawn = SaveMat("M_SpawnGlow", Lit(Color.black, 0f, 0.5f, null, null, null, new Color(0.85f, 0.08f, 0.05f)));
        var mPillarRune = SaveMat("M_PillarRune", Lit(Color.black, 0f, 0.5f, null, null, null, new Color(1.3f, 0.45f, 3.0f)));
        var mTorch = SaveMat("M_TorchGlow", Lit(new Color(0.3f, 0.1f, 0f), 0f, 0.5f, null, null, null, new Color(3.5f, 1.3f, 0.25f)));
        var mMetal = SaveMat("M_Metal", Lit(new Color(0.18f, 0.18f, 0.2f), 0.85f, 0.65f, null, null, null, Color.black));
        var mBlade = SaveMat("M_VoidBlade", Lit(new Color(0.3f, 0.8f, 1f), 0f, 0.9f, null, null, null, new Color(0.5f, 2.3f, 4.8f)));
        var robotMat = AssetDatabase.LoadAssetAtPath<Material>(RobotMat);
        var hero = new Material(robotMat);
        hero.SetColor("_BaseColor", new Color(0.88f, 0.95f, 1f));
        hero.EnableKeyword("_EMISSION");
        hero.SetColor("_EmissionColor", Color.black);
        hero.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        var mHero = SaveMat("M_Hero", hero);

        // ------------------------------------------------------------ animator (copy with IK pass enabled)
        string ctrlPath = Root + "/Animation/HS_Character.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) == null) AssetDatabase.CopyAsset(SrcController, ctrlPath);
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        var layers = ctrl.layers;
        layers[0].iKPass = true;
        ctrl.layers = layers;
        EditorUtility.SetDirty(ctrl);

        // ------------------------------------------------------------ meshes
        var runeBand = SaveMesh(Annulus("Mesh_RuneBand", 6.0f, 7.6f, 128, 24f));
        var edgeRing = SaveMesh(Annulus("Mesh_EdgeRing", 17.55f, 18.0f, 160, 1f));
        var ringA = SaveMesh(Annulus("Mesh_RingA", 2.55f, 2.68f, 96, 1f));
        var ringB = SaveMesh(Annulus("Mesh_RingB", 4.55f, 4.64f, 112, 1f));
        var ringC = SaveMesh(Annulus("Mesh_RingC", 8.6f, 8.72f, 140, 1f));
        var spawnRing = SaveMesh(Annulus("Mesh_SpawnRing", 0.9f, 1.04f, 48, 1f));
        var spawnRingIn = SaveMesh(Annulus("Mesh_SpawnRingInner", 0.45f, 0.52f, 32, 1f));

        // ------------------------------------------------------------ scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Skyboxes/SkyNight.mat");
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.27f, 0.42f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.016f;
        RenderSettings.fogColor = new Color(0.07f, 0.05f, 0.12f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.6f;

        var moonGo = new GameObject("Moonlight");
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.62f, 0.68f, 1f);
        moon.intensity = 1.35f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.85f;
        moon.lightmapBakeType = LightmapBakeType.Realtime;
        moonGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
        RenderSettings.sun = moon;

        // arena
        var arena = new GameObject("Arena").transform;
        var floor = Prim(PrimitiveType.Cylinder, "Floor", arena, new Vector3(0f, -0.5f, 0f), new Vector3(36f, 0.5f, 36f), mFloor, true);
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.AddComponent<MeshCollider>();
        Prim(PrimitiveType.Cylinder, "Cliff", arena, new Vector3(0f, -4.7f, 0f), new Vector3(36.6f, 4f, 36.6f), mStoneDark, false);
        Prim(PrimitiveType.Cylinder, "CliffBase", arena, new Vector3(0f, -10f, 0f), new Vector3(30f, 3f, 30f), mStoneDark, false);

        var deco = new GameObject("FloorDecals").transform;
        deco.SetParent(arena, false);
        MeshObj("EdgeRing", deco, edgeRing, mEdge, 0.02f);
        var band = MeshObj("RuneBand", deco, runeBand, mRunes, 0.015f);
        band.AddComponent<Spinner>().degreesPerSecond = 4f;
        MeshObj("RingA", deco, ringA, mInner, 0.018f);
        MeshObj("RingB", deco, ringB, mInner, 0.018f);
        MeshObj("RingC", deco, ringC, mEdge, 0.018f);
        var ticks = new GameObject("Glyphs").transform;
        ticks.SetParent(deco, false);
        ticks.gameObject.AddComponent<Spinner>().degreesPerSecond = -9f;
        for (int i = 0; i < 24; i++)
        {
            float a = i * 15f;
            var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            var t = Prim(PrimitiveType.Cube, "Glyph", ticks, dir * 3.6f + Vector3.up * 0.012f, new Vector3(0.08f, 0.01f, i % 2 == 0 ? 0.7f : 0.35f), mInner, false);
            t.transform.localRotation = Quaternion.LookRotation(dir);
            t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // pillars & torches
        var pillars = new GameObject("Pillars").transform;
        pillars.SetParent(arena, false);
        for (int i = 0; i < 10; i++)
        {
            float ang = i * 36f + 18f;
            var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            var p = new GameObject("Pillar_" + i).transform;
            p.SetParent(pillars, false);
            p.position = dir * 15.6f;
            p.rotation = Quaternion.LookRotation(-dir);
            bool broken = i % 3 == 1;
            float colH = broken ? 1.5f : 2.7f;
            Prim(PrimitiveType.Cube, "Base", p, new Vector3(0f, 0.25f, 0f), new Vector3(1.8f, 0.5f, 1.8f), mStone, true);
            Prim(PrimitiveType.Cylinder, "Column", p, new Vector3(0f, 0.5f + colH, 0f), new Vector3(1.1f, colH, 1.1f), mStone, true);
            Prim(PrimitiveType.Cylinder, "RuneBandLow", p, new Vector3(0f, 1.3f, 0f), new Vector3(1.15f, 0.05f, 1.15f), mPillarRune, false);
            if (!broken)
            {
                Prim(PrimitiveType.Cylinder, "RuneBandHigh", p, new Vector3(0f, 0.5f + 2f * colH - 0.7f, 0f), new Vector3(1.15f, 0.05f, 1.15f), mPillarRune, false);
                float top = 0.5f + 2f * colH;
                Prim(PrimitiveType.Cube, "Cap", p, new Vector3(0f, top + 0.18f, 0f), new Vector3(1.6f, 0.36f, 1.6f), mStone, false);
                Prim(PrimitiveType.Cylinder, "Brazier", p, new Vector3(0f, top + 0.5f, 0f), new Vector3(0.85f, 0.14f, 0.85f), mMetal, false);
                Prim(PrimitiveType.Cylinder, "Coals", p, new Vector3(0f, top + 0.62f, 0f), new Vector3(0.6f, 0.04f, 0.6f), mTorch, false).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                FX.Fire(p, mGlow, new Vector3(0f, top + 0.65f, 0f), 1.3f);
                var lgo = new GameObject("TorchLight");
                lgo.transform.SetParent(p, false);
                lgo.transform.localPosition = new Vector3(0f, top + 1.5f, 0f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.22f);
                l.range = 12f;
                l.intensity = 2.6f;
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                var f = lgo.AddComponent<Flicker>();
                f.baseIntensity = 2.6f;
            }
            else
            {
                var capFallen = Prim(PrimitiveType.Cube, "FallenCap", p, new Vector3(1.4f, 0.3f, 1.2f), new Vector3(1.6f, 0.36f, 1.6f), mStone, false);
                capFallen.transform.localRotation = Quaternion.Euler(8f, 35f, 14f);
                for (int r = 0; r < 4; r++)
                {
                    var rub = Prim(PrimitiveType.Cube, "Rubble", p, new Vector3(Random.Range(-1.6f, 1.6f), 0.12f, Random.Range(0.6f, 2f)), Vector3.one * Random.Range(0.2f, 0.45f), mStone, false);
                    rub.transform.localRotation = Random.rotation;
                }
            }
        }

        // floating rocks in the void
        var rocks = new GameObject("FloatingRocks").transform;
        rocks.SetParent(arena, false);
        for (int i = 0; i < 12; i++)
        {
            float ang = i * 30f + Random.Range(-10f, 10f);
            var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            var rock = Prim(PrimitiveType.Cube, "Rock_" + i, rocks, dir * Random.Range(24f, 36f) + Vector3.up * Random.Range(-4f, 5f),
                new Vector3(Random.Range(1.5f, 4f), Random.Range(1f, 3f), Random.Range(1.5f, 4f)), mStone, false);
            rock.transform.localRotation = Random.rotation;
            var sp = rock.AddComponent<Spinner>();
            sp.degreesPerSecond = Random.Range(-6f, 6f);
            sp.bobAmplitude = Random.Range(0.3f, 0.8f);
            sp.bobSpeed = Random.Range(0.3f, 0.7f);
        }

        FX.Embers(arena, mGlow, 17f);

        // spawn points
        var spawnRoot = new GameObject("SpawnPoints").transform;
        var spawnPoints = new List<Transform>();
        for (int i = 0; i < 8; i++)
        {
            var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            var sp = new GameObject("Spawn_" + i).transform;
            sp.SetParent(spawnRoot, false);
            sp.position = dir * 11.2f;
            MeshObj("Sigil", sp, spawnRing, mSpawn, 0.014f);
            MeshObj("SigilInner", sp, spawnRingIn, mSpawn, 0.014f);
            spawnPoints.Add(sp);
        }

        // post-processing
        var profile = NewProfile("VP_VoidArena");
        var bloom = AddVC<Bloom>(profile);
        bloom.threshold.value = 0.9f; bloom.intensity.value = 1.3f; bloom.scatter.value = 0.72f;
        var tone = AddVC<Tonemapping>(profile);
        tone.mode.value = TonemappingMode.ACES;
        var vig = AddVC<Vignette>(profile);
        vig.intensity.value = 0.36f; vig.smoothness.value = 0.45f; vig.color.value = new Color(0.03f, 0f, 0.06f);
        var color = AddVC<ColorAdjustments>(profile);
        color.postExposure.value = 0.45f; color.contrast.value = 18f; color.saturation.value = 14f;
        var chroma = AddVC<ChromaticAberration>(profile);
        chroma.intensity.value = 0.06f;
        var grain = AddVC<FilmGrain>(profile);
        grain.intensity.value = 0.16f;
        var volGo = new GameObject("Global Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;
        vol.sharedProfile = profile;

        var hitProfile = NewProfile("VP_HitPulse");
        var hitChroma = AddVC<ChromaticAberration>(hitProfile);
        hitChroma.intensity.value = 1f;
        var lens = AddVC<LensDistortion>(hitProfile);
        lens.intensity.value = -0.32f;
        var hitColor = AddVC<ColorAdjustments>(hitProfile);
        hitColor.postExposure.value = 0.35f; hitColor.saturation.value = 25f;
        var hitGo = new GameObject("Hit Pulse Volume");
        var hitVol = hitGo.AddComponent<Volume>();
        hitVol.isGlobal = true;
        hitVol.priority = 10f;
        hitVol.weight = 0f;
        hitVol.sharedProfile = hitProfile;

        // player
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
        smr.sharedMaterial = mHero;
        var ik = model.AddComponent<IKRelay>();
        ik.rig = rig;
        ik.isPlayer = true;
        var sword = Weapons.Sword(player.transform, mMetal, mBlade);
        sword.localPosition = new Vector3(0.34f, 1.0f, 0.3f);
        sword.localRotation = Quaternion.LookRotation(new Vector3(0.12f, 0.62f, 1f).normalized, Vector3.up);
        rig.weapon = sword;
        rig.bladeLength = 1.255f;
        var pc = player.AddComponent<PlayerController>();
        pc.animator = anim;
        pc.model = model.transform;
        pc.rig = rig;
        pc.body = smr;
        var heroLightGo = new GameObject("HeroLight");
        heroLightGo.transform.SetParent(player.transform, false);
        heroLightGo.transform.localPosition = new Vector3(0f, 2.4f, 0.3f);
        var heroLight = heroLightGo.AddComponent<Light>();
        heroLight.type = LightType.Point;
        heroLight.color = new Color(0.45f, 0.8f, 1f);
        heroLight.range = 5.5f;
        heroLight.intensity = 1.6f;
        heroLight.shadows = LightShadows.None;
        heroLight.lightmapBakeType = LightmapBakeType.Realtime;

        // camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 250f;
        cam.clearFlags = CameraClearFlags.Skybox;
        camGo.AddComponent<AudioListener>();
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.antialiasingQuality = AntialiasingQuality.High;
        var camRig = camGo.AddComponent<CameraRig>();
        camRig.target = player.transform;
        var camRot = Quaternion.Euler(camRig.pitch, camRig.yaw, 0f);
        camGo.transform.SetPositionAndRotation(Vector3.up - camRot * Vector3.forward * camRig.distance, camRot);

        // managers
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gmGo.AddComponent<Sfx>();
        var hudGo = new GameObject("HUD");
        var hud = hudGo.AddComponent<HUD>();
        gm.player = pc;
        gm.cameraRig = camRig;
        gm.hud = hud;
        gm.hitVolume = hitVol;
        gm.spawnPoints = spawnPoints.ToArray();
        gm.robotModel = AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx);
        gm.characterController = ctrl;
        gm.robotMaterial = robotMat;
        gm.glowMaterial = mGlow;
        gm.trailMaterial = mFlat;
        gm.metalMaterial = mMetal;
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
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player;
        if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("[Hack & Slash] Void Arena built at " + ScenePath + ". Press Play (or Hack & Slash > Play Void Arena).");
    }

    // ---------------------------------------------------------------- helpers

    static void Folders()
    {
        foreach (var f in new[] { "Materials", "Meshes", "Scenes", "Settings", "Animation" })
            if (!AssetDatabase.IsValidFolder(Root + "/" + f)) AssetDatabase.CreateFolder(Root, f);
    }

    static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tiles + file);

    static Texture2D Normal(string file)
    {
        var imp = AssetImporter.GetAtPath(Tiles + file) as TextureImporter;
        return imp != null && imp.textureType == TextureImporterType.NormalMap ? Tex(file) : null;
    }

    static Material Lit(Color baseColor, float metallic, float smooth, Texture2D baseMap = null, Vector2? tiling = null,
        Texture2D normal = null, Color? emission = null, Texture2D emissionMap = null)
    {
        var m = new Material(lit);
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        if (baseMap) m.SetTexture("_BaseMap", baseMap);
        if (tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
        if (normal)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            if (emissionMap) m.SetTexture("_EmissionMap", emissionMap);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
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
            existing.vertices = m.vertices;
            existing.normals = m.normals;
            existing.uv = m.uv;
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

    static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, float y)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
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
