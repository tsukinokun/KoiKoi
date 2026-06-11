using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 場札（フィールド）のカードの追加・整列・管理を担当するビューコンポーネント
/// </summary>
public class FieldView : MonoBehaviour
{
    [Header("Layout Settings")]
    [SerializeField] private float xSpacing = 1.2f;
    [SerializeField] private float ySpacing = 1.5f;

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.4f; // 🌟移動にかける時間を共通化

    /// <summary>
    /// 場にカードを新しく追加し、自動で再整列させる
    /// </summary>
    public void AddCard(Card card, bool isFaceUp = true)
    {
        if (card == null) return;

        // 1️⃣ 移動前の「元の世界座標」をキープしたまま親を変更する
        // (SetParentしても世界座標を維持するUnityのデフォルト挙動を利用)
        card.transform.SetParent(this.transform, worldPositionStays: true);
        card.SetFaceUp(isFaceUp);

        // 2️⃣ 🌟全カードの再整列・補間移動アニメーションを開始
        Rearrange();
    }

    /// <summary>
    /// 🌟すべての場札を現在の枚数に応じたグリッド位置へ「ぬるっと」移動させる
    /// </summary>
    public void Rearrange()
    {
        int childCount = this.transform.childCount;
        if (childCount == 0) return;

        int maxColumns = 4;
        if (childCount > 8) maxColumns = Mathf.CeilToInt(childCount / 2f);

        int index = 0;
        foreach (Transform child in this.transform)
        {
            Card card = child.GetComponent<Card>();
            if (card == null) continue;

            // --- 1. 目標となるローカル座標の計算 ---
            int column = index % maxColumns;
            int row = index / maxColumns;

            float x = (column - (maxColumns - 1) / 2f) * xSpacing;
            float y = -(row * ySpacing) + (ySpacing / 2f);

            // Z座標（描画順）の計算も維持
            Vector3 targetLocalPos = new Vector3(x, y, -0.05f * index);

            // --- 2. 🌟ローカル座標から「目標の世界座標」を割り出す ---
            // TransformPointを使うことで、親(FieldView)の場所がどこであっても正確な世界座標に変換できます
            Vector3 targetWorldPos = this.transform.TransformPoint(targetLocalPos);

            // 描画順（Zソート）と回転だけは、並び替えの瞬間に確定させておく
            // (これをしないと、移動中にカードが前後に重なるバグが起きる可能性があります)
            Vector3 currentLocalPos = card.transform.localPosition;
            currentLocalPos.z = targetLocalPos.z;
            card.transform.localPosition = currentLocalPos;
            card.transform.localRotation = Quaternion.identity;

            // --- 3. 🌟全カードに対して補間移動を命令する ---
            // 新しく追加されたカードも、元からあったカードも、
            // 「今の自分の位置」から「新しく計算された目標の世界座標」へ向かって一斉に滑り出します
            card.MoveToPositionAsync(targetWorldPos, moveDuration, card.GetCancellationTokenOnDestroy()).Forget();

            index++;
        }
    }
}