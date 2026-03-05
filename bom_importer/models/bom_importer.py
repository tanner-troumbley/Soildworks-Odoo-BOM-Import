from odoo import models, api, fields
import json

class BOMImporterLine(models.Model):
    _name="bom.importer.line"
    _description="BOM Impoter Mapping"            

    name = fields.Text(string='SW Property', copy=False, store=True, required=True)
    field_id = fields.Many2one('ir.model.fields', domain='[("model_id", "ilike", "product.product")]', string='Odoo Field', copy=False, store=True, required=True, ondelete='cascade')
    bom_importer_id = fields.Many2one('bom.importer', ondelete="cascade", required=True)
    
class BOMImporter(models.Model):
    _name = 'bom.importer'
    _description = 'BOM Importer'
    _sql_constraints = [
            (
                'name',
                'UNIQUE(name)',
                'The Name should be unique.',
            ),
        ]
    
    name = fields.Text(string='Name', copy=False, store=True, required=True)
    line_ids = fields.One2many("bom.importer.line", "bom_importer_id")
    
    @api.model
    def import_bom_json(self, bom_json_str, record_name=None):
        """Import entire BOM in one transaction with field mapping and reporting"""
        bom_data = json.loads(bom_json_str)
        property_field_map = {}
        if record_name:
            record = self.env['bom.importer'].search([('name', '=', record_name)], limit=1)
            if record:
                # Gives a dict of swkey: odooKey
                for line in record.line_ids:
                    property_field_map |= {line.name: line.field_id.name}

        Product = self.env['product.product']

        report = {
            "products_created": [],
            "products_updated": [],
            "boms_created": [],
            "missing_fields": []
        }

        def get_or_create_product(swproduct):
            """Gets or Creats Odoo Product while mapping the fields set with bom.importer.lines if a record_name is providied."""
            # This is useful if the Data from the Json is mapped without transformation.
            vals = {}
            for swKey, odooKey in property_field_map.items():
                vals[odooKey] = swproduct['Properties'].get(swKey, False)
                
            product = Product.search([('name', '=', swproduct['Name'])], limit=1)
            vals = {}
            for key, value in property_field_map.items():
                vals[key] = property_field_map.get(value, False)
               
            if product:
                if vals:
                    product.write(vals)
                    report["products_updated"].append(swproduct['Name'])
            else:
                # Can create a bom.importer.line that ties Description to prdocut name or hardcode it.
                # vals['name'] = swproduct["Properties"]['Description']
                
                # This needs to be hardcoded as we only mapp properties not things outside of it.
                vals['default_code'] = swproduct['Name']
                vals['type'] = 'product'
                product = Product.create(vals)
                report["products_created"].append(swproduct['Name'])
            return product

        def process_assembly(assembly):
            assembly_product = get_or_create_product(assembly)
            bom_lines = []
            for part in assembly['Components']:
                part_product = get_or_create_product(part)
                bom_lines.append((0, 0, {
                    'product_id': part_product.id,
                    'product_qty': part['Quantity']
                }))
                if part['IsAssembly']:
                    process_assembly(part)
            if bom_lines:
                bom = self.env['mrp.bom'].create({
                    'product_tmpl_id': assembly_product.product_tmpl_id.id,
                    'product_qty': part['Quantity'],
                    'type': 'normal',
                    'bom_line_ids': bom_lines
                })
                report["boms_created"].append((assembly, bom))

        process_assembly(bom_data)
        return report
