using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    [Tooltip("現在のお題データ")]
    public MoleculeData CurrentTargetData;

    // Moleculeから通知を受け取るメソッド
    public void CheckMoleculeMatch(Molecule submittedMolecule)
    {
        // 1. submittedMolecule の構造をスキャンする
        // 2. CurrentTargetData と一致しているかチェックする
        // 3. 一致していればクリア処理、またはベンゼン環への置換処理！

        Debug.Log("Managerが判定を実行中...");
    }
}