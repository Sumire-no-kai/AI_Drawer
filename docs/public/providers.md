# Provider status

Provider websites can change independently of AI Drawer. A provider appearing in the application is a candidate entry, not a guarantee that every login method or feature works.

| Provider | Current evidence label | Important limitation |
| --- | --- | --- |
| ChatGPT | Experimental | Early basic-use evidence exists; full login/session, files, recovery, and purchase-boundary Gates remain open. |
| Claude | Experimental | One Google sign-in popup returned successfully; repeat coverage and checkout evidence remain open. |
| Gemini | Experimental | One environment passed several basic flows; long-conversation, download, generic external-link, renderer-failure, and repeat-environment checks remain open. |
| Grok | Limited | Reply text rendered blank in one tested embedded environment; purchase and narrowed X-origin evidence remain open. |
| DeepSeek | Experimental | Basic use was reported; detailed Gates remain open. |
| Doubao / 豆包 (China) | Experimental | Basic use was reported; checkout evidence remains open. |
| Qwen Studio (International) | Experimental | Basic use was reported; detailed Gates remain open. |
| Tongyi Qianwen / 通义千问 (China) | Not tested | Separate regional product and profile; full matrix required. |
| GLM / 智谱清言 (China) | Experimental | Basic use was reported; detailed Gates remain open. |
| Z.ai (International) | Not tested | Separate regional product and profile; full matrix required. |
| Microsoft Copilot (Personal) | Not tested | Full authentication, conversation, persistence, navigation, and purchase matrix required. |

`Experimental` means the provider can be opened but is incompletely verified. `Limited` means a material limitation is known. `Not tested` means no compatibility claim is made. The versioned evidence source is the repository's Provider Compatibility Matrix.
