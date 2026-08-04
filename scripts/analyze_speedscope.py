import json

def main():
    filepath = "artifacts/parquet_profile_opt.speedscope.json"
    with open(filepath, "r") as f:
        data = json.load(f)
    
    frames = data.get("shared", {}).get("frames", [])
    frame_names = [f.get("name", "") for f in frames]
    
    profiles = data.get("profiles", [])
    if not profiles:
        print("No profiles found.")
        return
        
    p0 = profiles[0]
    events = p0.get("events", [])
    
    # Calculate duration per frame
    stack = [] # (frame_idx, start_time)
    durations = {}
    
    for ev in events:
        ev_type = ev.get("type")
        frame_idx = ev.get("frame")
        at = ev.get("at", 0)
        
        if ev_type == "O": # Open
            stack.append((frame_idx, at))
        elif ev_type == "C": # Close
            if stack:
                top_frame, start_at = stack.pop()
                dur = at - start_at
                name = frame_names[top_frame] if top_frame < len(frame_names) else f"Frame_{top_frame}"
                durations[name] = durations.get(name, 0) + dur

    total_time = sum(durations.values())
    
    print("=" * 100)
    print(f"🔥 HOTSPOT PROFILING ANALYSIS (Speedscope Event Trace - {len(events)} Stack Events)")
    print("=" * 100)
    print(f"{'PERCENT':<10} | {'TOTAL DURATION (ms)':<20} | {'METHOD / COMPONENT NAME'}")
    print("=" * 100)
    
    sorted_durations = sorted(durations.items(), key=lambda x: x[1], reverse=True)
    for name, dur in sorted_durations[:30]:
        pct = (dur / total_time) * 100 if total_time > 0 else 0
        print(f"{pct:6.2f}%    | {dur / 1000.0:18.2f} ms | {name}")

if __name__ == "__main__":
    main()
