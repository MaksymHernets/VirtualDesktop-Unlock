# Quest 3 AV1 bitrate feasibility

Investigated 2026-10-22 against the installed Virtual Desktop Streamer 1.34.23.0. This is a feasibility investigation, not a working bitrate-unlock feature. The running Streamer, installed helper, headset and Windows settings were left unchanged.

## Conclusion

A PC-side experimental AV1 target override around 250 Mbps looks technically feasible. The installed Streamer contains a software-defined 200 Mbps ceiling for Quest 3, and its bitrate calculation accepts values beyond the normal slider range. This does not demonstrate that a real 250 Mbps stream will reach the headset or decode reliably. The native encoder path, network bandwidth limiter, headset decoder, resolution and refresh rate must still be tested together.

## Local evidence

- The headset enum's display-name metadata identifies numeric value 1000 as Meta Quest 3 and 1001 as Meta Quest 3S.
- Quest 3's maximum calculation returns 200,000,000 bits/second for AV1 and AV1 10-bit.
- Shared settings include `SharedUserSettings.VRBitrateLimit` and computed `SharedMobileSettings.AV1VRBitrate`.
- The normalized slider value is rounded to three decimal places and interpolated between 10 Mbps and the codec maximum; the inspected calculation does not clamp it to 0–1.
- `UpdateVRBitrates()` also applies the measured-bandwidth minimum. A later recalculation can overwrite a one-time field change.
- `OculusStreamer` reads the computed AV1 bitrate and forwards it to the encoding wrappers. This path has stream splitting and spacewarp adjustments, so the target property alone is not a measurement of actual transmitted traffic.

The original calculation was invoked in an isolated Windows PowerShell process; no encoder or headset session was started:

| Codec | Normalized slider value | Calculated target |
|---|---:|---:|
| AV1 / AV1 10-bit | 1.000 | 200 Mbps |
| AV1 / AV1 10-bit | 1.250 | 247.5 Mbps |
| AV1 / AV1 10-bit | approximately 1.263158 | 249.97 Mbps |

The `AV1VRBitrate` property also accepted exactly 250,000,000 on an isolated, uninitialized settings object. Its inspected setter only assigns the backing field. Neither result proves actual encoding or decoding at that rate.

## Hardware and documentation

[Qualcomm's XR2 Gen 2 product brief](https://docs.qualcomm.com/doc/87-73689-1/87-73689-1_REV_A_Snapdragon_XR2_Gen_2_Platform_Product_Brief.pdf) lists AV1 decode, 8K60 video decode and low-latency slice decoding. It does not establish a guaranteed 250 Mbps low-latency Quest 3 VR operating point.

[Meta's media requirements](https://developers.meta.com/horizon/documentation/android-apps/media-requirements/) identify Quest 3 and above as supporting AV1. The document covers media playback requirements, so it cannot validate high-refresh PCVR streaming at a particular bitrate.

[Android's low-latency decoder documentation](https://source.android.com/docs/core/media/low-latency-media) describes a decoder mode specifically intended for real-time applications. Decode throughput, buffering and low-latency behavior must be evaluated together; faster Wi-Fi alone does not establish decoder feasibility.

## Appropriate next experiment

Extend the existing startup helper with an explicit opt-in, Quest-3-only AV1 target override, defaulting off. Prefer targeting the AV1 result to changing the shared slider value, which also affects other codec targets. Reapply at the correct update points, preserve automatic network limiting, and restore normal targets when disabled or disconnected.

Compare 200, 225 and 250 Mbps at the same scene, resolution, refresh rate, codec and spacewarp state. Confirm the active codec and actual bitrate, then record encoder, network and decoder latency, dropped frames, stutter and visual changes. Verify that reconnects and slider changes do not silently reset the override. A headset slider still displaying 200 would not by itself prove or disprove a PC-side target override.

The subsequent 0.1.4 implementation exposes an explicit opt-in override and tests the original server calculation and forwarding into encoder wrappers. Actual headset throughput and decoding remain unverified until the end-to-end test is performed.
