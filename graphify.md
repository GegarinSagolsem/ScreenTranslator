## graphify — check the graph before re-reading code

`graphify-out/` in this folder holds a code-only knowledge graph (local AST, no API key, no LLM).
At the start of a session, read `graphify-out/GRAPH_REPORT.md`, and answer "where is / what calls /
how does X" with the graph before opening files:

```bash
graphify query "how does the verifier build the graded set"   # BFS over graph.json
graphify explain "_build"                                       # a node and its neighbours
graphify path "_build" "compose"                                # shortest path between two nodes
```

Refresh after code changes — local and free, run from this folder, in this order:

```bash
graphify . --code-only     # re-extract code AST
graphify cluster-only .    # re-cluster, rewrite GRAPH_REPORT.md, graph.json, graph.html
```

Do not run `graphify extract` with an LLM backend (`--backend claude-cli` etc.) unless the user
asks: the docs (`*.md`, workflows) are deliberately not in the graph, so read those directly.
