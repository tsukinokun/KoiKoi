using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

/// <summary>
/// 役名・点数・成立条件と成立例の札の一覧（役一覧画面）を表示する
/// </summary>
public class YakuListPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RectTransform rowContainer;
    [SerializeField] private GameObject rowTemplate; // Info/Header/Name・Info/Header/Points・Info/Condition の Text と、札を並べる Cards を持つ行（非アクティブにしておく）
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private SpriteAtlas cardAtlas;
    [SerializeField] private Image cardImageTemplate; // 成立例の札1枚分（非アクティブにしておく）
    [SerializeField] private bool pauseGameWhileOpen; // 対局中は開いている間ゲームを一時停止する

    /// <summary>開いている間は true（札のクリックなど、UI以外の入力を止めるために使う）</summary>
    public static bool IsOpen { get; private set; }

    private float _timeScaleBeforeOpen = 1f;
    private bool _isPausing;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        BuildRows();
    }

    private void BuildRows()
    {
        if (rowContainer == null || rowTemplate == null) return;

        foreach (YakuCatalog.Entry entry in YakuCatalog.Entries)
        {
            GameObject row = Instantiate(rowTemplate, rowContainer);
            row.name = $"Row_{entry.Name}";
            SetText(row, "Info/Header/Name", entry.Name);
            SetText(row, "Info/Header/Points", $"{entry.Points}文");
            SetText(row, "Info/Condition", entry.Condition);
            AddExampleCards(row.transform.Find("Cards"), entry.ExampleCardIds);
            row.SetActive(true);
        }
    }

    private void AddExampleCards(Transform cardsParent, string[] cardIds)
    {
        if (cardsParent == null || cardAtlas == null || cardImageTemplate == null || cardIds == null) return;

        foreach (string id in cardIds)
        {
            Sprite sprite = cardAtlas.GetSprite(id);
            if (sprite == null)
            {
                Debug.LogWarning($"YakuListPanel: 札のスプライトが見つかりません: {id}");
                continue;
            }

            Image card = Instantiate(cardImageTemplate, cardsParent);
            card.name = id;
            card.sprite = sprite;
            card.gameObject.SetActive(true);
        }
    }

    private static void SetText(GameObject row, string childPath, string value)
    {
        Transform child = row.transform.Find(childPath);
        Text text = child != null ? child.GetComponent<Text>() : null;
        if (text != null) text.text = value;
    }

    public void Open()
    {
        if (panelRoot == null || IsOpen) return;

        panelRoot.SetActive(true);
        IsOpen = true;

        // 開くたびに一番上の役から見せる
        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f;
        }

        if (pauseGameWhileOpen)
        {
            _timeScaleBeforeOpen = Time.timeScale;
            Time.timeScale = 0f;
            _isPausing = true;
        }
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        ReleaseOpenState();
    }

    private void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    private void OnDisable()
    {
        // 開いたままシーンが切り替わった場合でも、一時停止と入力ブロックを残さない
        ReleaseOpenState();
    }

    private void ReleaseOpenState()
    {
        IsOpen = false;
        if (_isPausing)
        {
            Time.timeScale = _timeScaleBeforeOpen;
            _isPausing = false;
        }
    }
}
