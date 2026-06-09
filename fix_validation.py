import re

with open('src/Commands/EngineeringDashboard.lsp', 'r') as f:
    lsp_content = f.read()

# Let's inspect the RunBeamArrayDialog
print(lsp_content[lsp_content.find('(defun RunBeamArrayDialog'):])
