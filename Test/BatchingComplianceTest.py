#!/usr/bin/env python3
"""
Comprehensive test suite to ensure all direct tree update calls are eliminated
and replaced with batching interface across the entire Vigor project
"""

import os
import re
import glob
from typing import List, Dict, Tuple

class BatchingComplianceTest:
    def __init__(self, project_root: str):
        self.project_root = project_root
        self.violations = []
        self.allowed_patterns = [
            # Allowed patterns that don't need batching
            r'\.GetFloat\(',
            r'\.GetBool\(',
            r'\.GetInt\(',
            r'\.GetString\(',
            r'\.GetTreeAttribute\(',
            # Allowed in BatchedTreeAttribute itself
            r'_tree\.Set',  # Internal implementation
            # Allowed in test files
            r'Test.*\.cs:.*\.Set',
        ]
        
    def find_cs_files(self) -> List[str]:
        """Find all C# files in the project"""
        cs_files = []
        for root, dirs, files in os.walk(self.project_root):
            # Skip certain directories
            if any(skip in root for skip in ['bin', 'obj', '.git', '.vs']):
                continue
            for file in files:
                if file.endswith('.cs'):
                    cs_files.append(os.path.join(root, file))
        return cs_files
    
    def scan_file_for_violations(self, file_path: str) -> List[Dict]:
        """Scan a single file for direct tree update calls"""
        violations = []
        
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()
                lines = content.split('\n')
        except Exception as e:
            print(f"Error reading {file_path}: {e}")
            return violations
        
        # Patterns to detect direct tree updates that bypass batching
        violation_patterns = [
            (r'(\w+)\.SetFloat\s*\(', 'SetFloat'),
            (r'(\w+)\.SetBool\s*\(', 'SetBool'),
            (r'(\w+)\.SetInt\s*\(', 'SetInt'),
            (r'(\w+)\.SetString\s*\(', 'SetString'),
            (r'(\w+)\.MarkPathDirty\s*\(', 'MarkPathDirty'),
            (r'SetDebug(?:Float|Bool|Int|String)\s*\(', 'SetDebug'),
        ]
        
        # Patterns to exclude (legitimate uses)
        exclusion_patterns = [
            r'_batchedStaminaTree\.',  # Using batching interface
            r'batchedTree\.',          # Using batching interface
            r'new TreeAttribute\(\)',  # Creating new TreeAttribute for events
            r'eventArgs\.',            # Event argument setup
            r'var eventArgs = new TreeAttribute', # Event creation
            r'BatchedTreeAttribute\.cs', # Inside BatchedTreeAttribute class file
            r'_watchedAttributes\.MarkPathDirty', # Internal batching calls
            r'// Inside BatchedTreeAttribute', # Comments indicating internal use
            r'class BatchedTreeAttribute', # Inside the batching class itself
        ]
        
        # Exclude entire BatchedTreeAttribute.cs file (it's the batching implementation)
        if 'BatchedTreeAttribute.cs' in file_path:
            return violations
            
        # Exclude test files that legitimately need to call base methods
        if '\\Test\\' in file_path or '/Test/' in file_path:
            return violations
            
        for line_num, line in enumerate(lines, 1):
            # Check if this line should be excluded
            should_exclude = False
            for exclusion_pattern in exclusion_patterns:
                if re.search(exclusion_pattern, line):
                    should_exclude = True
                    break
            
            if should_exclude:
                continue
                
            for pattern, violation_type in violation_patterns:
                matches = re.finditer(pattern, line)
                for match in matches:
                    violations.append({
                        'file': file_path,
                        'line': line_num,
                        'content': line.strip(),
                        'type': violation_type,
                        'match': match.group(0)
                    })
        
        return violations
    
    def _is_allowed_pattern(self, full_line: str) -> bool:
        """Check if a line matches allowed patterns"""
        for pattern in self.allowed_patterns:
            if re.search(pattern, full_line):
                return True
        return False
    
    def run_full_scan(self) -> Dict:
        """Run complete compliance scan across all C# files"""
        print("=" * 80)
        print("VIGOR PROJECT BATCHING COMPLIANCE TEST")
        print("=" * 80)
        
        cs_files = self.find_cs_files()
        print(f"Scanning {len(cs_files)} C# files...")
        
        all_violations = []
        files_with_violations = 0
        
        for file_path in cs_files:
            violations = self.scan_file_for_violations(file_path)
            if violations:
                all_violations.extend(violations)
                files_with_violations += 1
        
        # Group violations by type
        violations_by_type = {}
        for violation in all_violations:
            vtype = violation['type']
            if vtype not in violations_by_type:
                violations_by_type[vtype] = []
            violations_by_type[vtype].append(violation)
        
        # Print results
        print(f"\nSCAN RESULTS:")
        print(f"Files scanned: {len(cs_files)}")
        print(f"Files with violations: {files_with_violations}")
        print(f"Total violations: {len(all_violations)}")
        
        if all_violations:
            print(f"\n🔴 BATCHING COMPLIANCE FAILED!")
            print(f"Found {len(all_violations)} direct tree update calls that bypass batching:")
            
            for vtype, violations in violations_by_type.items():
                print(f"\n{vtype} violations ({len(violations)}):")
                for violation in violations[:10]:  # Show first 10 of each type
                    rel_path = os.path.relpath(violation['file'], self.project_root)
                    print(f"  {rel_path}:{violation['line']} - {violation['content']}")
                if len(violations) > 10:
                    print(f"  ... and {len(violations) - 10} more")
        else:
            print(f"\n✅ BATCHING COMPLIANCE PASSED!")
            print("No direct tree update calls found - all calls use batching interface")
        
        return {
            'passed': len(all_violations) == 0,
            'total_violations': len(all_violations),
            'violations_by_type': violations_by_type,
            'files_scanned': len(cs_files),
            'files_with_violations': files_with_violations
        }
    
    def generate_replacement_script(self, violations: List[Dict]) -> str:
        """Generate a script to help replace violations with batching calls"""
        script_lines = [
            "# Replacement suggestions for batching compliance",
            "# Review each suggestion before applying",
            ""
        ]
        
        for violation in violations:
            rel_path = os.path.relpath(violation['file'], self.project_root)
            script_lines.append(f"# {rel_path}:{violation['line']}")
            script_lines.append(f"# OLD: {violation['content']}")
            
            # Generate replacement suggestion
            if violation['type'] == 'SetFloat':
                script_lines.append(f"# NEW: _batchedTree.SetFloat(...)")
            elif violation['type'] == 'SetBool':
                script_lines.append(f"# NEW: _batchedTree.SetBool(...)")
            elif violation['type'] == 'MarkPathDirty':
                script_lines.append(f"# NEW: _batchedTree.TrySync()")
            elif violation['type'] == 'SetDebug':
                script_lines.append(f"# NEW: _batchedTree.SetFloat/SetBool(...) // Remove SetDebug wrapper")
            
            script_lines.append("")
        
        return '\n'.join(script_lines)

def main():
    project_root = r"C:\Projects\Vigor"
    tester = BatchingComplianceTest(project_root)
    
    # Run the compliance test
    results = tester.run_full_scan()
    
    # Generate replacement script if violations found
    if not results['passed']:
        print(f"\nGenerating replacement suggestions...")
        all_violations = []
        for violations in results['violations_by_type'].values():
            all_violations.extend(violations)
        
        replacement_script = tester.generate_replacement_script(all_violations)
        
        script_path = os.path.join(project_root, 'batching_replacements.txt')
        with open(script_path, 'w') as f:
            f.write(replacement_script)
        print(f"Replacement suggestions written to: {script_path}")
    
    print("=" * 80)
    return 0 if results['passed'] else 1

if __name__ == "__main__":
    exit(main())
