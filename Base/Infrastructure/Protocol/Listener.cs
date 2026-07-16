using Base.Services;

namespace Base.Protocol;

// Matches an incoming frame against a leading Command/Key pattern and, on a hit,
// fills every registered Structure from the frame body and raises OnTriggered.
// (Promoted from the CommonProtocol feature so it is shared infrastructure.)
public class Listener(byte[] pattern)
{
	private readonly byte[] pattern = pattern;
	public event Action<Listener, ReadOnlyMemory<byte>, DateTime> OnTriggered = delegate { };
	public readonly List<Structure> structures = new();

	public void Match(ReadOnlyMemory<byte> data, DateTime time)
	{
		if (ProtocolService.IsCmdMatch(pattern, data.Span))
		{
			ReadOnlySpan<byte> span = data.Span[1..];
			foreach (Structure structure in structures)
			{
				structure.Parse(span);
			}
			OnTriggered(this, data, time);
		}
	}

	public bool TryGet<T>(out T structure) where T : Structure
	{
		foreach (Structure t in structures)
		{
			if (t is T to)
			{
				structure = to;
				return true;
			}
		}
		structure = null;
		return false;
	}
}
