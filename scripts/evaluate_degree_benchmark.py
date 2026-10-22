"""Execute the fixed, local analysis notebook cells with Python; no kernel or paid API needed."""

import contextlib
import io
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NOTEBOOK = ROOT / "benchmarks/degree_planning/analysis.ipynb"


def main():
    notebook = json.loads(NOTEBOOK.read_text(encoding="utf-8"))
    namespace = {"__name__": "__main__"}
    count = 0
    for cell in notebook["cells"]:
        if cell["cell_type"] != "code":
            continue
        count += 1
        stream = io.StringIO()
        # This executes reviewed repository code, never retrieved policy or model content.
        with contextlib.redirect_stdout(stream):
            exec(compile("".join(cell["source"]), str(NOTEBOOK), "exec"), namespace)
        output = stream.getvalue()
        cell["execution_count"] = count
        cell["outputs"] = [
            {"output_type": "stream", "name": "stdout", "text": output.splitlines(keepends=True)}
        ]
        print(output, end="")
    NOTEBOOK.write_text(json.dumps(notebook, indent=2) + "\n", encoding="utf-8")
    print(f"Executed {count} analysis cells successfully.")


if __name__ == "__main__":
    main()
