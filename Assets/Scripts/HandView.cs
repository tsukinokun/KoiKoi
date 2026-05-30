using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 手札のカードの追加・整列・回転（扇形配置）を担当するビューコンポーネント
/// </summary>
public class HandView : MonoBehaviour
{
    [Header("Layout Settings")]
    [SerializeField] private bool isEnemy = false;    // プレイヤー用ならfalse、NPC用ならtrueにする
    [SerializeField] private float radius = 12.0f;     // 円の半径
    [SerializeField] private float angleStep = 5.0f;   // カード間の角度

    /// <summary>
    /// 手札にカードを追加し、自動で扇形に再整列させる
    /// </summary>
    public void AddCard(Card card, bool isFaceUp)
    {
        if (card == null) return;

        // 自身のGameObject（HandParent）を親にする
        card.transform.SetParent(this.transform);
        card.SetFaceUp(isFaceUp);

        // 追加されたので並び替える
        Rearrange();
    }

    /// <summary>
    /// 手札にある全カードを取得し、現在のインデックスに基づいて扇形にきれいに並べ直す
    /// </summary>
    public void Rearrange()
    {
        int childCount = this.transform.childCount;
        if (childCount == 0) return;

        int index = 0;
        foreach (Transform child in this.transform)
        {
            Card card = child.GetComponent<Card>();
            if (card == null) continue;

            // プレイヤーは90度（真上）、敵は270度（真下）を基準にする
            float baseAngle = isEnemy ? 270.0f : 90.0f;

            // 中央を基準に扇形に展開（カード枚数に応じた中央オフセットは childCount をベースに計算）
            float centerOffset = (childCount - 1) / 2f;
            float currentAngle = baseAngle + (index - centerOffset) * angleStep * (isEnemy ? 1 : -1);
            float rad = currentAngle * Mathf.Deg2Rad;

            float x = Mathf.Cos(rad) * radius;
            // 敵は半径分「上」へ、プレイヤーは「下」へオフセット
            float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

            // Z軸をインデックスに応じて僅かにずらし、重ね合わせの描画順を制御
            card.transform.localPosition = new Vector3(x, y, -0.01f * index);

            // 回転：敵なら下を向くように調整
            float rotationOffset = isEnemy ? 270.0f : 90.0f;
            card.transform.localRotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);

            index++;
        }
    }
}