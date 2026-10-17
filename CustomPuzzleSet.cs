using System;
using System.Collections.Generic;

namespace TPFDS;

[Serializable]
public class CustomPuzzleSetData {
	public MetaData meta { get; set; }
	public ActData[] acts;
	public PuzzleGroupData[] puzzleGroups { get; set; }
	public Dictionary<string, PuzzleData> puzzles { get; set; }
	public Dictionary<string, DialogueEvent> banter { get; set; }
	public Dictionary<string, MeetingData> meetings { get; set; }
	public Dictionary<string, SpeechBubbleData[]> conversations { get; set; }
	public int format_version { get; set; }
}

[Serializable]
public class MetaData {
	public string name { get; set; }
	public string author { get; set; }
}

[Serializable]
public class ActData {
	public string act;
	public string name;
}

[Serializable]
public class PuzzleGroupData {
	public string title { get; set; }
	public string[] storyline { get; set; }
}

[Serializable]
public class PuzzleData {
	public int[] message { get; set; }
	public int[][] validResponses { get; set; }
}

[Serializable]
public class DialogueEvent {
	[Serializable]
	public class DictDefinition {
		public int signal;
		public string word;
	}

	[Serializable]
	public class Condition {
		#nullable enable
		// Taco: NOTE: In most cases the explicit `= null` is not needed
		// C# is dumb though, so I'd rather not risk it
		public string? puzzleIs { get; set; } = null!;
		public string? afterPuzzle { get; set; } = null!;
		public string? beforePuzzle { get; set; } = null!;
		public float? timeOnPuzzle { get; set; } = null!;
		public float? sessionTime { get; set; } = null!;
		public int? fails { get; set; } = null!;
		public int? hintsReceived { get; set; } = null!;
		public string? answerHas { get; set; } = null!;
		public string? submittedAndWaiting { get; set; } = null!;
		public string? submittedCorrect { get; set; } = null!;
		public string? submittedWrong { get; set; } = null!;
		public int[]? submittedAndWaitingSigs { get; set; } = null!;
		public int[]? submittedCorrectSigs { get; set; } = null!;
		public int[]? submittedWrongSigs { get; set; } = null!;
		public string? changedWordTo { get; set; } = null!;
		public string? changedWordFrom { get; set; } = null!;
		public DictDefinition? changedSignalTo { get; set; } = null!;
		public DictDefinition? changedSignalFrom { get; set; } = null!;
		public DictDefinition? signalIs { get; set; } = null!;
		public DictDefinition? signalContains { get; set; } = null!;
		#nullable disable
	}

	public string name { get; set; } = "Banter";
	public bool isHint { get; set; }
	public Condition[] triggers { get; set; }
	public string[] conversation { get; set; }
	public bool disabled { get; set; }
}

[Serializable]
public class MeetingData {
	public bool progressAct { get; set; }
	public bool progressWeek { get; set; }
	public string room { get; set; }
	public string[] conversations { get; set; }
	public Dictionary<string, string> journals { get; set; }
}

[Serializable]
public class SpeechBubbleData {
	public string speaker { get; set; }
	public string text { get; set; }
}
