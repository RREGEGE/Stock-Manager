namespace Portfolio.Core;

// 종목 마스터 1건. 코드는 6자리 숫자만이 아니라 영문 혼합(예: 0001A0), ETN 7자리(예: Q500067)도 있다.
public sealed record SymbolInfo(string SymbolCode, string SymbolName, string Market);
