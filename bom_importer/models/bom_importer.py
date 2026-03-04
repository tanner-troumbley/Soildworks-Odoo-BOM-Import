from odoo import models, api, fields
import json

class BOMImporterLine(models.Model):
    _name="bom.impoter.line"
    _description="BOm Impoter Mapping"            

    name = fields.Text(string='Original Field', copy=False, store=True, required=True)
    field_id = fields.Many2one('ir.model.fields', domian='[("model_id", "ilike", "product.product")]', string='Odoo Field', copy=False, store=True, required=True)
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
    line_ids = fields.One2many("bom.impoter.line", "bom_importer_id")
    
    @api.model
    def import_bom_json(self, bom_json_str):
        """Import entire BOM in one transaction with field validation and reporting"""
        bom_data = json.loads(bom_json_str)
        property_field_map = { self.line_ids.filed_id.mapped('name'): self.line_ids.mapped('name') }

        Product = self.env['product.product']

        report = {
            "products_created": [],
            "products_updated": [],
            "boms_created": [],
            "missing_fields": []
        }

        def get_or_create_product(product):
            product_code = product['Name']
            if product['Properties'].get('Revision', False):
                product_code +=  product['Properties']['Revision'].encode("utf-8").decode("unicode_escape")
                
            product = Product.search([('default_code', '=', product_code)], limit=1)
            vals = {}
            for key, value in property_field_map.items():
                vals[key] = property_field_map.get(value, False)
               
            if product:
                if vals:
                    product.write(vals)
                    report["products_updated"].append(product_code)
            else:
                vals['name'] = product["Properties"]['Description']
                vals['type'] = 'product'
                product = Product.create(vals)
                report["products_created"].append(product_code)
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
            if bom_lines:
                bom = self.env['mrp.bom'].create({
                    'product_tmpl_id': assembly_product.product_tmpl_id.id,
                    'product_qty': part['Quantity'],
                    'type': 'normal',
                    'bom_line_ids': bom_lines
                })
                report["boms_created"].append(assembly)
            if assembly['IsAssembly']:
                process_assembly(assembly)

        process_assembly(bom_data)
        return report
