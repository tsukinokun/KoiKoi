using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks; // 🌟UniTaskを使用するために追加

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

        // 1️⃣ 🌟移動前の「現在の世界座標（World Position）」を一時保存する
        Vector3 startWorldPos = card.transform.position;

        // 自身のGameObject（FieldParent）を親にする
        card.transform.SetParent(this.transform);
        card.SetFaceUp(isFaceUp);

        // 2️⃣ 🌟並び替え処理を呼ぶ（新しく追加されたカードも含めて最終座標を計算させる）
        // ※ただし、新しく追加されたこのcardだけは一瞬でワープさせずに、戻り先を記憶させます。
        Rearrange(exceptCard: card);

        // 3️⃣ 🌟「新しく追加されたカード」の、Rearrangeによって決まった目標ローカル座標を世界座標に変換する
        Vector3 targetWorldPos = card.transform.position;

        // 4️⃣ 🌟一瞬でジャンプするのを防ぐため、見た目を移動前の元の世界座標に強制的に引き戻す！
        card.transform.position = startWorldPos;

        // 5️⃣ 🌟カード自身のスクリプトに「引き戻した位置から、目標の座標まで0.4秒かけて滑らかに動け」と命令する
        card.MoveToPositionAsync(targetWorldPos, 0.4f, card.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 場札を現在の枚数に応じてグリッド状にきれいに並べ直す
    /// 🌟引数に exceptCard を追加し、新しく追加されてアニメーションさせたいカードを即時上書きから除外できるように拡張
    /// </summary>
    public void Rearrange(Card exceptCard = null)
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

            // 🌟計算された目標のローカル位置
            Vector3 targetLocalPos = new Vector3(x, y, -0.05f * index);

            // 🌟新しく追加されたカード（アニメーションさせたいカード）の場合
            if (card == exceptCard)
            {
                // positionを直接書き換えるとワープしてしまうので、
                // 一旦目標のローカル座標を代入して、AddCard側で「目標の世界座標」を取得させるために利用します
                card.transform.localPosition = targetLocalPos;
                card.transform.localRotation = Quaternion.identity;
            }
            else
            {
                // 既に場にある他のカードは、通常通り即座に整列させる
                // (※もし既存の場札もズレた時にぬるっと動かしたい場合は、ここもMoveToPositionAsyncにできますが、まずは追加時のみで試します)
                card.transform.localPosition = targetLocalPos;
                card.transform.localRotation = Quaternion.identity;
            }

            index++;
        }
    }
}