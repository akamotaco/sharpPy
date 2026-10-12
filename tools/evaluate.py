"""Run existing suites in isolated processes; retain outputs and explicit failures.

Requires CPython 3.12. Timing here is diagnostic process duration, not a VM benchmark.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]


def stable_output(name, output):
    """Mask only named volatile fields; retain raw output and captured values."""
    patterns = {
        "test_python312_missing_features.py": [
            r"^(  (?:Memoryview slice|Read-only memoryview|Read-only slice): <memory at )(?P<value>0x[0-9a-fA-F]+)(>)$",
        ],
        "examples/advanced.py": [
            r"^(Execution time: )(?P<value>[0-9.eE+-]+)( seconds)$",
        ],
        "test_ultimate_complex_features.py": [
            r"^(  🕐 TimestampMixin initialized at )(?P<value>[0-9.eE+-]+)()$",
            r"^(  ⚡ advanced_process executed in )(?P<value>[0-9.eE+-]+)(s)$",
            r"^(🎬 Session Ultimate Test completed in )(?P<value>[0-9.eE+-]+)(s)$",
            r"^(   .*'age_seconds': )(?P<value>[0-9.eE+-]+)(, 'results_count': .*)$",
        ],
    }
    fields = []
    lines = []
    for number, line in enumerate(output.splitlines(keepends=True), 1):
        for pattern in patterns.get(name.replace('\\', '/'), []):
            match = re.fullmatch(pattern, line.rstrip('\r\n'))
            if match:
                value = match['value']
                if value.startswith('0x'):
                    valid = int(value, 16) > 0
                else:
                    try:
                        valid = math.isfinite(float(value)) and float(value) >= 0
                    except ValueError:
                        valid = False
                if valid:
                    fields.append(dict(line=number, value=value))
                    start, end = match.span('value')
                    line = line[:start] + '<volatile>' + line[end:]
                break
        lines.append(line)
    return ''.join(lines), fields


def failure_markers(output):
    markers = []
    for line in output.splitlines():
        if re.search(r"(?:Failed:|FAIL(?:ED)?\s*[:=])\s*[1-9]\d*|[1-9]\d*\s+(?:FAILED|test\(s\) failed)", line, re.I):
            markers.append(line)
        elif (re.search(r"^\s*(?:FAIL:|FAILED:)", line) or line.startswith("❌")) and not re.search(r"Failed:\s*0\b", line, re.I):
            markers.append(line)
    return markers


def run(command, timeout, env):
    start = time.perf_counter()
    try:
        process = subprocess.run(command, cwd=ROOT, env=env, capture_output=True,
                                 encoding="utf-8", errors="replace", timeout=timeout)
        result = dict(exit_code=process.returncode, stdout=process.stdout,
                      stderr=process.stderr, timed_out=False)
    except subprocess.TimeoutExpired as error:
        def decode(value):
            return value.decode("utf-8", errors="replace") if isinstance(value, bytes) else value or ""
        result = dict(exit_code=None, stdout=decode(error.stdout), stderr=decode(error.stderr), timed_out=True)
    result["process_seconds"] = time.perf_counter() - start
    result["failure_markers"] = failure_markers(result["stdout"] + "\n" + result["stderr"])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--dll", default="bin/Release/net8.0/sharppy.dll")
    parser.add_argument("--output", default=".artifacts/evaluation/baseline.json")
    parser.add_argument("--timeout", type=float, default=30)
    parser.add_argument("--files", nargs="*")
    args = parser.parse_args()
    if sys.version_info[:2] != (3, 12):
        parser.error("Run this evaluator with the reference CPython 3.12 interpreter")
    files = args.files if args.files is not None else [
        *[str(path.relative_to(ROOT)) for path in sorted((ROOT / "tests").glob("level*.py"))],
        *[str(path.relative_to(ROOT)) for path in sorted(ROOT.glob("test_*.py"))],
        "tests/binary_inplace_regression.py",
        "tests/metaclass_attribute_regression.py",
        "tests/frozenset_hash_regression.py",
        "tests/set_numeric_identity_regression.py",
        "tests/integer_digit_limit_regression.py",
        "tests/cpython312_selected_regression.py",
        "tests/library_composition_regression.py",
        "tests/compatibility/bound_method_attributes.py",
        "tests/compatibility/class_body_exceptions.py",
        "tests/compatibility/class_locals_mapping.py",
        "tests/compatibility/coroutine_send.py",
        "tests/compatibility/dict_copy_subclass.py",
        "tests/compatibility/dict_iterator_same_size.py",
        "tests/compatibility/dict_retained_views.py",
        "tests/compatibility/dict_view_iterator_live_values.py",
        "tests/compatibility/memoryview_write.py",
        "tests/compatibility/module_locals_live.py",
        "tests/compatibility/module_mapping_pop.py",
        "tests/compatibility/metaclass_attribute_remaining.py",
        "tests/dict_key_identity_regression.py",
        "tests/dict_hash_protocol_regression.py",
        "tests/compatibility/numeric_dict_hash.py",
        "tests/compatibility/int_subclass_hash_protocol.py",
        "tests/numeric_mixed_comparison_regression.py",
        "tests/integer_subclass_comparison_regression.py",
        "tests/integer_string_parsing_regression.py",
        "tests/compatibility/int_subclass_large_string.py",
        "tests/comparison_dispatch_regression.py",
        "tests/integer_comparison_fastpath_regression.py",
        "tests/comparison_container_regression.py",
        "tests/native_comparison_slots_regression.py",
        "tests/type_setattr_binding_regression.py",
        "tests/sort_protocol_regression.py",
        "tests/compatibility/comparison_class_setattr.py",
        "tests/compatibility/comparison_remaining_slots.py",
        "tests/compatibility/integer_subclass_comparison_dispatch.py",
        "tests/compatibility/numeric_int_subclass_comparison.py",
        "tests/inplace_special_lookup.py",
        "tests/function_creation_regression.py",
        "tests/scope_binding_regression.py",
        "tests/builtin_descriptor_regression.py",
            "tests/bound_method_attributes_regression.py",
            "tests/memoryview_alias_regression.py",
            "tests/memoryview_release_regression.py",
            "tests/memoryview_index_regression.py",
            "tests/memoryview_slice_assignment_regression.py",
            "tests/slice_large_bounds_regression.py",
            "tests/bytes_comparison_regression.py",
            "tests/slice_assignment_indices_regression.py",
            "tests/range_slice_indices_regression.py",
            "tests/range_native_boundary_regression.py",
            "tests/range_bigint_regression.py",
            "tests/range_slice_step_regression.py",
            "tests/instruction_offsets_regression.py",
            "tests/generator_wide_jump_regression.py",
            "tests/cache_execution_regression.py",
            "tests/compatibility/generator_throw_loop_resume.py",
            "tests/compatibility/generator_throw_expression_stack.py",
            "tests/exception_stack_depth_regression.py",
            "tests/compatibility/exception_context_after_handler.py",
            "tests/exception_state_regression.py",
            "tests/compatibility/exception_state_in_callee.py",
            "tests/exception_caller_state_regression.py",
            "tests/compatibility/generator_close_exception.py",
            "tests/generator_close_regression.py",
            "tests/compatibility/generator_close_delegation.py",
            "tests/generator_delegation_close_regression.py",
            "tests/compatibility/generator_delegate_iterator.py",
            "tests/generator_send_protocol_regression.py",
            "tests/iter_protocol_regression.py",
            "tests/metaclass_iterator_protocol_regression.py",
            "tests/class_lexical_closure_regression.py",
            "tests/super_name_analysis_regression.py",
            "tests/super_frame_regression.py",
            "tests/constant_return_cleanup_regression.py",
            "tests/cfg_reachability_regression.py",
            "tests/finally_scope_regression.py",
            "tests/unbound_variable_regression.py",
            "tests/generator_kind_regression.py",
            "tests/coroutine_send_regression.py",
            "tests/coroutine_wrapper_send_regression.py",
            "tests/coroutine_close_regression.py",
            "tests/coroutine_throw_regression.py",
            "tests/exception_constructor_arguments_regression.py",
            "tests/lookup_exception_constructor_regression.py",
            "tests/key_error_regression.py",
            "tests/os_error_arguments_regression.py",
            "tests/os_error_subtype_regression.py",
            "tests/os_error_winerror_regression.py",
            "tests/os_error_winerror_subclass_regression.py",
            "tests/os_error_remaining_types_regression.py",
            "tests/os_error_errno_subclass_regression.py",
            "tests/integer_hash_regression.py",
            "tests/os_error_directory_regression.py",
            "tests/blocking_io_arguments_regression.py",
            "tests/compatibility/exception_specialized_arguments.py",
            "tests/compatibility/coroutine_throw_empty_args.py",
            "tests/generator_throw_delegation_regression.py",
            "tests/compatibility/coroutine_throw_yield_from.py",
            "tests/compatibility/coroutine_throw_delegation.py",
            "tests/unraisable_close_regression.py",
            "tests/compatibility/coroutine_close_lookup_error.py",
            "tests/compatibility/unraisable_traceback_format.py",
            "tests/compatibility/coroutine_close_cleanup.py",
            "tests/compatibility/coroutine_nested_send.py",
            "tests/compatibility/coroutine_initial_send.py",
            "tests/compatibility/unbound_fast_local.py",
            "tests/compatibility/continue_finally_stack.py",
            "tests/compatibility/nested_finally_return_duplicate.py",
            "tests/compatibility/super_captured_first_argument.py",
            "tests/compatibility/super_alias_class_cell.py",
            "tests/compatibility/metaclass_bound_method_closure.py",
            "tests/compatibility/metaclass_invalid_iterator.py",
            "tests/sequence_iterator_protocol_regression.py",
            "tests/compatibility/generator_invalid_iterator.py",
            "tests/compatibility/iterator_lookup_error_fallback.py",
            "tests/compatibility/range_large_step.py",
            "tests/slice_indices_bigint_regression.py",
            "tests/sequence_large_step_regression.py",
            "tests/bytearray_iterable_assignment_regression.py",
            "tests/bytearray_slice_reentry_regression.py",
            "tests/bytearray_bytevalue_regression.py",
            "tests/stop_iteration_arguments_regression.py",
            "tests/exception_args_update_regression.py",
            "tests/exception_args_length_hint_regression.py",
            "tests/operator_length_hint_regression.py",
            "tests/memoryview_large_step_regression.py",
            "tests/compatibility/slice_large_step.py",
            "tests/compatibility/operator_length_hint.py",
            "tests/compatibility/exception_args_length_hint.py",
            "tests/compatibility/stop_iteration_args_update.py",
            "tests/compatibility/stop_iteration_message.py",
            "tests/compatibility/bytearray_slice_callback_order.py",
            "tests/compatibility/bytearray_slice_iterable_assignment.py",
            "tests/slice_read_step_regression.py",
            "tests/slice_mutation_step_regression.py",
            "tests/slice_delete_compaction_regression.py",
            "tests/compatibility/slice_step_iteration_overflow.py",
            "tests/compatibility/bytes_slice_equality.py",
            "tests/frame_lifetime_regression.py",
            "tests/locals_observation_regression.py",
            "tests/cell_delete_regression.py",
            "tests/dynamic_locals_regression.py",
            "tests/namespace_locals_regression.py",
            "tests/locals_refresh_regression.py",
            "tests/class_locals_mapping_regression.py",
            "tests/prepared_namespace_regression.py",
            "tests/type_new_dict_subclass_regression.py",
            "tests/call_failure_state_regression.py",
            "tests/class_closure_mapping_regression.py",
            "tests/class_exception_table_regression.py",
            "tests/class_nested_state_regression.py",
            "tests/exception_offset_regression.py",
            "tests/compatibility/lookup_error_builtin.py",
            "tests/dict_pop_order_regression.py",
            "tests/dict_copy_regression.py",
            "tests/dict_copy_subclass_regression.py",
            "tests/dict_views_regression.py",
            "tests/dict_iterator_regression.py",
            "tests/dict_cursor_mutation_regression.py",
            "tests/module_mapping_mutation_regression.py",
            "tests/module_mapping_clear_regression.py",
            "tests/module_mapping_live_reads_regression.py",
            "tests/module_mapping_observation_regression.py",
        "examples/hello.py", "examples/advanced.py",
    ]
    env = dict(os.environ, PYTHONUTF8="1", PYTHONHASHSEED="0")
    env["DOTNET_ROOT"] = str(Path(args.dotnet).resolve().parent)
    report = dict(python=sys.version, executable=sys.executable, dotnet=str(Path(args.dotnet).resolve()),
                  dotnet_info=run([args.dotnet, "--info"], args.timeout, env),
                  git=run(["git", "rev-parse", "HEAD"], args.timeout, env)["stdout"].strip(),
                  git_status=run(["git", "status", "--short"], args.timeout, env)["stdout"],
                  dll_sha256=hashlib.sha256((ROOT / args.dll).read_bytes()).hexdigest(),
                  timeout=args.timeout, cases=[])
    output = ROOT / args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    commands = [(name, [sys.executable, name], [args.dotnet, args.dll, name]) for name in files]
    if args.files is None:
        commands += [(flag, None, [args.dotnet, args.dll, flag]) for flag in
                     ("--test-generator-kwargs", "--test-pymethod-kwargs")]
    for name, reference_command, sharppy_command in commands:
        reference = run(reference_command, args.timeout, env) if reference_command else None
        sharppy = run(sharppy_command, args.timeout, env)
        volatile_fields = None
        if sharppy["timed_out"]:
            status = "sharppy_timeout"
        elif reference and reference["timed_out"]:
            status = "reference_timeout"
        elif reference and (reference["exit_code"] != 0 or reference["failure_markers"]):
            status = "reference_test_failure"
        elif sharppy["exit_code"] != 0 or sharppy["failure_markers"]:
            status = "sharppy_failure"
        elif reference and (reference["stdout"] != sharppy["stdout"] or reference["stderr"] != sharppy["stderr"]):
            status = "output_difference"
            ref_stable, ref_fields = stable_output(name, reference['stdout'])
            actual_stable, actual_fields = stable_output(name, sharppy['stdout'])
            if (ref_fields and ref_stable == actual_stable and
                    reference['stderr'] == sharppy['stderr']):
                status = 'matching_except_volatile_fields'
                volatile_fields = dict(reference=ref_fields, sharppy=actual_fields)
        else:
            status = "matching_output" if reference else "no_reported_failure"
        report["cases"].append(dict(name=name, status=status, reference=reference, sharppy=sharppy,
                                  volatile_fields=volatile_fields))
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{name}: {status}", flush=True)
    counts = {}
    for case in report["cases"]:
        counts[case["status"]] = counts.get(case["status"], 0) + 1
    report["counts"] = counts
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(counts, ensure_ascii=False))
    return int(any(case["status"] in ("output_difference", "sharppy_timeout", "sharppy_failure", "reference_timeout", "reference_test_failure")
                   for case in report["cases"]))


if __name__ == "__main__":
    raise SystemExit(main())
