using System;
using System.Collections.Generic;

namespace TPFDS;

[Serializable]
public class CustomPuzzleSetData {
	public MetaData meta { get; set; }
	public PuzzleGroupData[] puzzleGroups { get; set; }
	public Dictionary<string, PuzzleData> puzzles { get; set; }
	public WordReactionData[] words { get; set; }
	public Dictionary<string, SpeechBubbleData[]> conversations { get; set; }
}

[Serializable]
public class MetaData {
	public string name { get; set; }
	public string author { get; set; }
}

[Serializable]
public class PuzzleGroupData {
	public string title { get; set; }
	public string[] puzzles { get; set; }
	public (string before, string after) meetings { get; set; }
}

[Serializable]
public class PuzzleData {
	[Serializable]
	public class Hint {
		public int after_tries { get; set; }
		public int[] on_repsonse { get; set; }
	}
	
	public int[] message { get; set; }
	public int[][] validResponses { get; set; }
	public (string before, Hint[] hints) conversations { get; set; }
}

[Serializable]
public class WordReactionData {
	public int word { get; set; }
	public Dictionary<string, string[]> reactions { get; set; }
}

[Serializable]
public class SpeechBubbleData {
	public string speaker { get; set; }
	public string text { get; set; }
}
