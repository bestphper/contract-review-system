import { Col, Form, Input, InputNumber, Row, Select } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { api } from './api';

export const contractTypes=['采购合同','销售合同','服务合同','租赁合同','劳动合同','保密协议','其他'];
export function ContractFields(){
  const {data:parties=[],isLoading:partiesLoading}=useQuery({queryKey:['parties','options'],queryFn:()=>api.parties('')});
  const {data:users=[]}=useQuery({queryKey:['org-users','options'],queryFn:api.orgUsers});
  const {data:departments=[]}=useQuery({queryKey:['departments'],queryFn:api.departments});
  const partyOptions=parties.map(p=>({label:`${p.name}（${p.type}）`,value:p.name}));
  return <><Row gutter={[16,0]} style={{marginInline:0}}>
    <Col xs={24} md={12}><Form.Item name="title" label="合同标题" rules={[{required:true,whitespace:true,message:'请输入合同标题'}]}><Input/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="contractType" label="合同类型"><Select showSearch allowClear options={contractTypes.map(value=>({label:value,value}))}/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="partyA" label="甲方主体" rules={[{required:true,message:'请选择甲方主体'}]}><Select showSearch loading={partiesLoading} optionFilterProp="label" options={partyOptions}/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="partyB" label="乙方主体" rules={[{required:true,message:'请选择乙方主体'}]}><Select showSearch loading={partiesLoading} optionFilterProp="label" options={partyOptions}/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="owner" label="负责人"><Select showSearch allowClear optionFilterProp="label" options={users.map(u=>({label:`${u.displayName}（${u.department}）`,value:u.displayName}))}/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="department" label="所属部门"><Select showSearch allowClear optionFilterProp="label" options={departments.map(d=>({label:d.name,value:d.name}))}/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="currency" label="币种"><Select options={['CNY','USD','EUR'].map(value=>({label:{CNY:'人民币 CNY',USD:'美元 USD',EUR:'欧元 EUR'}[value],value}))}/></Form.Item></Col>
    <Col xs={24} md={12}><Form.Item name="amount" label="合同金额"><InputNumber min={0} precision={2} style={{width:'100%'}}/></Form.Item></Col>
    <Col xs={24} md={8}><Form.Item name="signedAt" label="签署日期"><Input type="date"/></Form.Item></Col>
    <Col xs={24} md={8}><Form.Item name="effectiveAt" label="生效日期"><Input type="date"/></Form.Item></Col>
    <Col xs={24} md={8}><Form.Item name="expiresAt" label="到期日期"><Input type="date"/></Form.Item></Col>
  </Row><Form.Item name="paymentTerms" label="付款条款"><Input.TextArea rows={3}/></Form.Item>
  <Form.Item name="description" label="合同说明"><Input.TextArea rows={4}/></Form.Item>
  <Form.Item name="tags" label="标签"><Input/></Form.Item></>;
}
