using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// คลังเสียงเอฟเฟกต์แบบ "วางไฟล์แล้วใช้ได้เลย": โหลดจาก Resources/Audio/SFX/ ตามชื่อ
/// - Play("weapon_switch") จะหาไฟล์ weapon_switch, weapon_switch_1, weapon_switch_2 ... แล้วสุ่มเล่น (ไม่ซ้ำอันเดิมติดกัน)
/// - ไม่มีไฟล์ = เงียบ ไม่ error จึงเรียกใช้ได้ก่อนที่จะมีไฟล์เสียงจริง
/// - เล่นผ่านกลุ่ม SFX ของ AudioManager (Slider SFX คุมได้) ถ้าไม่มี AudioManager ใช้ PlayClipAtPoint
/// รายชื่อเสียงและที่มา (CC0) ดูที่ Assets/Resources/Audio/CREDITS.md
/// </summary>
public static class SfxLibrary
{
    private const string Folder = "Audio/SFX/";

    private static readonly Dictionary<string, AudioClip[]> cache = new Dictionary<string, AudioClip[]>();
    private static readonly Dictionary<string, int> lastIndex = new Dictionary<string, int>();
    private static AudioSource fallbackSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
        lastIndex.Clear();
        fallbackSource = null;
    }

    /// <summary>คลิปทั้งหมดของชื่อนี้ (อาจเป็น array ว่าง)</summary>
    public static AudioClip[] GetClips(string name)
    {
        if (cache.TryGetValue(name, out AudioClip[] clips))
            return clips;

        List<AudioClip> found = new List<AudioClip>();

        AudioClip single = Resources.Load<AudioClip>(Folder + name);
        if (single != null)
            found.Add(single);

        for (int i = 1; i <= 12; i++)
        {
            AudioClip variant = Resources.Load<AudioClip>(Folder + name + "_" + i);
            if (variant == null)
            {
                if (i > 2) break; // อนุญาตให้เริ่มที่ _1 หรือ _2 แต่ไม่ไล่หาต่อถ้าขาดช่วง
                continue;
            }

            found.Add(variant);
        }

        clips = found.ToArray();
        cache[name] = clips;
        return clips;
    }

    public static AudioClip Pick(string name)
    {
        AudioClip[] clips = GetClips(name);
        if (clips.Length == 0)
            return null;

        if (clips.Length == 1)
            return clips[0];

        lastIndex.TryGetValue(name, out int last);
        int index = Random.Range(0, clips.Length - 1);
        if (index >= last) index++; // ข้ามอันที่เพิ่งเล่น
        lastIndex[name] = index;
        return clips[index];
    }

    public static bool Has(string name) => GetClips(name).Length > 0;

    /// <summary>เล่นเสียง 2D (UI / ผู้เล่น) คืน true ถ้ามีคลิป</summary>
    public static bool Play(string name, float volume = 1f)
    {
        AudioClip clip = Pick(name);
        if (clip == null)
            return false;

        GetSource().PlayOneShot(clip, volume);
        return true;
    }

    /// <summary>เล่นเสียง ณ ตำแหน่งในโลก (3D)</summary>
    public static bool Play(string name, Vector3 position, float volume = 1f)
    {
        AudioClip clip = Pick(name);
        if (clip == null)
            return false;

        AudioSource.PlayClipAtPoint(clip, position, volume);
        return true;
    }

    /// <summary>เล่นเสียง UI: ใช้ไฟล์ตามชื่อถ้ามี ไม่งั้นใช้เสียง Click/Hover เดิมของ AudioManager</summary>
    public static void PlayUI(string name, bool hoverFallback = false)
    {
        if (Play(name))
            return;

        if (AudioManager.Instance == null)
            return;

        if (hoverFallback) AudioManager.Instance.PlayUIHover();
        else AudioManager.Instance.PlayUIClick();
    }

    private static AudioSource GetSource()
    {
        if (fallbackSource != null)
            return fallbackSource;

        GameObject go = new GameObject("SfxLibrary (Auto)");
        Object.DontDestroyOnLoad(go);
        fallbackSource = go.AddComponent<AudioSource>();
        fallbackSource.playOnAwake = false;
        fallbackSource.spatialBlend = 0f;
        fallbackSource.ignoreListenerPause = true; // เสียง UI ยังดังตอน Pause
        AudioManager.RouteToSfx(fallbackSource);
        return fallbackSource;
    }
}
