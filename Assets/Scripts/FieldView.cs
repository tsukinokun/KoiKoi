using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 場札（フィールド）のカードの追加・整列・管理を担当するビューコンポーネント
/// </summary>
public class FieldView : MonoBehaviour
{
    [Header("Layout Settings")]
    [SerializeField] private float xSpacing = 1.2f;
    [SerializeField] private float ySpacing = 1.5f;

    /// <summary>
    /// 場にカードを新しく追加し、自動で再整列させる
    /// </summary>
    public void AddCard(Card card, bool isFaceUp = true)
    {
        if (card == null) return;

        // 自身のGameObject（FieldParent）を親にする
        card.transform.SetParent(this.transform);
        card.SetFaceUp(isFaceUp);

        // 追加されたので綺麗に並び替える
        Rearrange();
    }

    /// <summary>
    /// 場札を現在の枚数に応じてグリッド状にきれいに並べ直す（旧 RearrangeFieldCards）
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

            int column = index % maxColumns;
            int row = index / maxColumns;

            float x = (column - (maxColumns - 1) / 2f) * xSpacing;
            float y = -(row * ySpacing) + (ySpacing / 2f);

            // Unityの2D描画順（Zソート）の考慮もそのまま移植
            card.transform.localPosition = new Vector3(x, y, -0.05f * index);
            card.transform.localRotation = Quaternion.identity;

            index++;
        }
    }
}