using System.Collections.Generic;

namespace TPFDS;

using Data = (DictionaryHypotheses.TermCluster cluster, TermHypotheses[] hypotheses);

public static class CustomHypothesisRepository {
	static Dictionary<string, Dictionary<string, Data>> hypotheses = [];

	public static Dictionary<string, Data> GetAllFromSet(string name) => hypotheses[name];

    static bool HypothesisLoadError(string reason, string name, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Loading hypotheses {name} failed: {reason}. Skipping {data.meta.name}");
        return false;
    }

    public static bool TryAdd(CustomPuzzleSetData data) {
		var local_hypotheses = new Dictionary<string, Data>(capacity: data.hypotheses.Count);

		foreach (var kvp in data.hypotheses) {
			var name = kvp.Key;
			var groupedHypotheses = kvp.Value;

			var cluster = new List<int>(capacity: groupedHypotheses.Length);
			var hypos = new List<TermHypotheses>(capacity: groupedHypotheses.Length);

			foreach (var entry in groupedHypotheses) {
				if (entry.word >= 0)
					return HypothesisLoadError("word must be negative", name, data);

				cluster.Add(entry.word);
				hypos.Add(new TermHypotheses() {
					termID = entry.word,
					aGuess = entry.AkersHypothesis,
					bGuess = entry.BautistaHypothesis,
					cGuess = entry.CarrieHypothesis,
				});
			}

			local_hypotheses[name] = (new DictionaryHypotheses.TermCluster() { terms = cluster.ToArray() }, hypos.ToArray());
		}

		hypotheses[data.meta.name] = local_hypotheses;

		return true;
	} 
}
