using System.Collections.Generic;

namespace TPFDS;

public class CustomPuzzleSetData {
	public MetaData meta;
	public PuzzleGroupData[] puzzleGroups;
	public Dictionary<string, PuzzleData> puzzles;
	public WordReactionData[] words;
	public Dictionary<string, SpeechBubbleData[]> conversations;
}

public class MetaData {
	public string name;
	public string author;
}

public class PuzzleGroupData {
	public string title;
	public string[] puzzles;
	public (string before, string after) meetings;
}

public class PuzzleData {
	public class Hint {
		public int after_tries;
		public int[] on_repsonse;
	}
	
	public int[] message;
	public int[][] validResponses;
	public (string before, Hint[] hints) conversations;
}

public class WordReactionData {
	public int word;
	public Dictionary<string, string[]> reactions;
}

public class SpeechBubbleData {
	public string speaker;
	public string text;
}
