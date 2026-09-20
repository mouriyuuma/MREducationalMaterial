using System.Collections.Generic;
using UnityEngine;

// 責任：混成軌道（σ骨格）の方向と、多重結合で腕を開く角度を定義する
// 原子1個のローカルな形だけを扱い、シーン上のどこに置くかには関与しない
public static class HybridizationTable
{
    // 孤立電子対（結合に使われない電子対）の数。立体数を求めるのに使う
    public static int GetLonePairCount(string elementType)
    {
        switch (elementType)
        {
            case "O": return 2;
            case "S": return 2;
            case "N": return 1;
            default: return 0; // C, H
        }
    }

    // 立体数 ＝ σ結合の数 ＋ 孤立電子対の数。これで混成（sp/sp2/sp3）が決まる
    public static int GetStericNumber(string elementType, int sigmaCount)
    {
        return sigmaCount + GetLonePairCount(elementType);
    }

    // σ結合どうしのなす角
    public static float GetBondAngle(string elementType, int sigmaCount)
    {
        switch (GetStericNumber(elementType, sigmaCount))
        {
            case 0:
            case 1:
                return 0f;
            case 2: return 180f;   // sp  直線形
            case 3: return 120f;   // sp2 平面三角形
            default:               // sp3 正四面体
                // 孤立電子対は結合電子対より場所を取るため、実測の結合角はやや狭くなる
                if (elementType == "O" || elementType == "S") return 104.5f;
                if (elementType == "N") return 107f;
                return 109.47f;
        }
    }

    // σ骨格の並びを「基準軸からの極角θ・方位角φ」で返す。1本目は必ず基準軸そのもの(0,0)。
    // 炭素の sp3 なら (0,0) (109.47,0) (109.47,120) (109.47,240) となり、
    // これは Atom_Carbon.prefab の BondPoint の配置と一致する
    public static void GetSigmaAngles(string elementType, int sigmaCount, List<Vector2> result)
    {
        result.Clear();
        if (sigmaCount <= 0) return;

        result.Add(Vector2.zero);
        if (sigmaCount == 1) return;

        float theta = GetBondAngle(elementType, sigmaCount);

        switch (GetStericNumber(elementType, sigmaCount))
        {
            case 2: // sp 直線形
                result.Add(new Vector2(theta, 0f));
                break;

            case 3: // sp2 平面三角形（3本が同一平面に 120° ずつ）
                result.Add(new Vector2(theta, 0f));
                if (sigmaCount > 2) result.Add(new Vector2(theta, 180f));
                break;

            default: // sp3 正四面体
                result.Add(new Vector2(theta, 0f));
                if (sigmaCount > 2) result.Add(new Vector2(theta, 120f));
                if (sigmaCount > 3) result.Add(new Vector2(theta, 240f));
                break;
        }
    }

    // 多重結合の腕を σ方向からどれだけ開くか（度）。
    // 実際の結合長の比に cos で対応させている（C-C 1.54Å / C=C 1.34Å / C≡C 1.20Å）
    public static float GetSplayAngle(int bondOrder)
    {
        switch (bondOrder)
        {
            case 2: return 28f; // cos28° ≒ 0.88 → C=C は C-C の約88%の長さになる
            case 3: return 38f; // cos38° ≒ 0.79 → C≡C は C-C の約79%の長さになる
            default: return 0f; // 単結合は σ方向そのもの
        }
    }

    // 原子中心どうしの距離の倍率。腕の開き角から導かれるので、
    // 二重・三重ほど自動的に短くなる
    public static float GetBondLengthMultiplier(int bondOrder)
    {
        return Mathf.Cos(GetSplayAngle(bondOrder) * Mathf.Deg2Rad);
    }

    // 芳香環の結合長の倍率。ベンゼンの C-C は 1.39Å で、
    // 単結合 1.54Å と二重結合 1.34Å のちょうど中間にあたる（1.39 / 1.54 ≒ 0.90）
    public const float AromaticBondLengthMultiplier = 0.903f;

    // σ方向 sigma のまわりに、結合次数のぶんだけ腕を開いた方向を返す（バナナ結合）
    // splayAxis は開く向きを決める回転軸で、sigma と直交している必要がある
    public static void GetSplayDirections(Vector3 sigma, Vector3 splayAxis, int bondOrder, List<Vector3> result)
    {
        result.Clear();
        sigma = sigma.normalized;

        float splay = GetSplayAngle(bondOrder);
        if (bondOrder <= 1 || splay <= 0f)
        {
            result.Add(sigma);
            return;
        }

        if (bondOrder == 2)
        {
            // σ方向をはさんで対称に2本開く
            result.Add(Quaternion.AngleAxis(splay, splayAxis) * sigma);
            result.Add(Quaternion.AngleAxis(-splay, splayAxis) * sigma);
            return;
        }

        // 三重結合は σ方向を軸にした三脚状に3本
        Vector3 tilted = Quaternion.AngleAxis(splay, splayAxis) * sigma;
        for (int i = 0; i < 3; i++)
        {
            result.Add(Quaternion.AngleAxis(120f * i, sigma) * tilted);
        }
    }

    // ベクトルに直交する適当な単位ベクトルを1本作る（基準が他にないときの逃げ道）
    public static Vector3 AnyPerpendicular(Vector3 v)
    {
        Vector3 candidate = Mathf.Abs(Vector3.Dot(v.normalized, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        return Vector3.Normalize(Vector3.Cross(v, candidate));
    }
}
