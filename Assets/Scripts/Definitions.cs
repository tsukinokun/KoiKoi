using System;
using System.Collections.Generic;

// カード自体のデータ構造
[Serializable]
public class CardData
{
    public string id;
    public int month;
    public string type;
    public List<string> tags;
}

[Serializable]
public class CardList
{
    public List<CardData> cards;
}

// ヤクのデータ構造 ---
[Serializable]
public class YakuData
{
    public string name;
    public int priority;
    public string requiredType;
    public int requiredCount;
    public string requiredTag;
    public string mustIncludeTag;
    public string excludeTag;
    public int requiredMonth;
    public int score;
}

[Serializable]
public class YakuList
{
    public List<YakuData> yakus;
}